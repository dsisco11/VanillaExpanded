# Crafting menu provider systems proposal

Status: Draft for review. This document proposes architecture and migration work; it does not claim that the systems described below are implemented.

## 1. Purpose

Allow crafting-menu features to contribute isolated logic by registering provider instances with managers that own collections of those providers.

The managers compose the contributions. Individual providers implement feature rules. The session coordinates evaluation, projection, and interaction without branching on the identities of participating features.

Craftable-only is the first new consumer, not a special case built into the query pipeline. Search and category filtering should use the same extension mechanism.

The design covers three distinct responsibilities:

| System | Provider responsibility | Manager responsibility |
| --- | --- | --- |
| Filtering | Decide whether an evaluated recipe selection matches a feature's criteria. | Require all active filters to match. |
| Ordering | Compare two evaluated selections according to one ranking criterion. | Apply an explicitly ordered comparison chain and a stable final tie-breaker. |
| Classification | Describe a typed presentation property, such as ingredient availability. | Compose contributions according to the rules of the classification dimension. |

These are separate contracts, not implementations of a universal provider interface.

## 2. Current baseline and motivation

At the time of writing:

- [CraftingMenuSession](../VanillaExpanded/src/CraftingMenu/CraftingMenuSession.cs) owns the search string, selected category, allocations, output selection, and interaction freeze.
- [CraftingCatalogueView](../VanillaExpanded/src/CraftingMenu/CraftingCatalogueView.cs) resolves automatic or pinned alternatives, applies category and search predicates, and hard-codes result ordering.
- [GuiElementCraftingTile](../VanillaExpanded/src/CraftingMenu/GuiElementCraftingTile.cs) maps recipe evaluation directly to availability presentation.
- The current source does not contain the earlier craftable-only implementation. Its previous feature-specific boolean parameter and interaction-freeze branch motivate this proposal, but are not an assumed implementation baseline.

Adding a parameter and session branch for each future feature would make the orchestration layer responsible for every feature's rules. Favorites, Recent, alternative sort modes, and future recipe relevance rules would compound that coupling.

The existing [item-slot indicator providers](../VanillaExpanded/src/ItemSlotIndicators/IItemSlotIndicatorProvider.cs) demonstrate the repository's feature-provider pattern: a feature supplies data without owning rendering. Reuse that separation of responsibilities, not the indicator system's sampling, GPU resources, or priority semantics wholesale.

## 3. Goals and non-goals

### Goals

- Add or remove a feature's contribution through registration with the appropriate manager.
- Keep each provider's matching, comparison, or classification logic isolated and independently testable.
- Compose category, search, and craftable-only filtering without feature-specific query flags.
- Preserve the current default ordering, explicit alternative selection, allocation semantics, and native GUI behavior.
- Keep filtered membership correct during inventory updates, including while a recipe is hovered, focused, or pressed.
- Retain interaction stability without allowing a stale click to activate a replacement recipe.
- Establish ownership and deterministic composition rules before adding more consumers.

### Non-goals

- A public third-party mod API, runtime plugin discovery, reflection-based registration, or a dependency-injection framework.
- A generic provider framework shared across unrelated mod systems.
- New allocation rules, inventory sources, transfer permissions, or crafting execution behavior.
- Implementing Favorites, Recent, browse-state persistence, the sort selector, or new empty-state actions as part of this proposal.
- Automatically introducing every proposed manager before it has a concrete consumer.

## 4. Data and authority boundaries

The immutable-per-world catalogue remains the source of recipe and output metadata. Inventory-dependent allocation remains the source of ingredient availability.

Providers consume an evaluated `CraftingRecipeSelection`, including its selected recipe, evaluation, and available allocation data. They must not:

- Capture inventories, rerun allocation, or dispatch transfers.
- Mutate the catalogue, evaluation, selected alternative, or another provider's state.
- Read GUI controls, mouse state, rendering resources, or live item stacks to decide a rule.
- Use a presentation classification as a substitute for authoritative allocation facts.

In particular, craftable-only reads `Evaluation.CanCraft`; it does not infer craftability from border color, relevance, or the absence of an unsupported warning. `CanPlace` is not a substitute: it indicates supported placement, including partial placement.

Selection of an alternative remains the catalogue view's responsibility. In this proposal, filtering "selection" means selection of visible rows, not a new provider mechanism for choosing recipe variants.

## 5. Filter manager

### Contract and composition

Introduce an internal `RecipeFilterSet` wrapping a collection of `IRecipeFilter` instances. The essential filter operation is:

```csharp
bool Matches(CraftingRecipeSelection selection);
```

The boolean is the predicate's result, not a feature flag passed through orchestration methods.

- A row is eligible only when every registered provider matches it.
- No registered filters means every candidate is eligible.
- Providers may be short-circuited; correctness must not depend on invocation order or invocation count.
- Filter registration order must not affect membership.
- A provider must not reorder or manufacture rows.
- Alternative resolution happens before matching, so all filters inspect the same selected recipe for an output.

### Initial providers

| Provider | Owned criteria | Matching rule |
| --- | --- | --- |
| Search | A normalized snapshot of the search terms. | Every term matches the selected recipe's existing search text, preserving current whitespace and case behavior. |
| Category | One native category code, including the existing Other sentinel. | Preserve the current selected-recipe category behavior. All contributes no category restriction. |
| Craftable-only | No additional inventory state. | The selected recipe's evaluation reports `CanCraft`. |

Feature state changes replace the feature's criteria/provider registration as one logical update. Search normalization is performed when text changes, not once per candidate.

Empty search and All can be represented by absent registrations. The craftable-only feature owns one provider instance and registers it while enabled, removing its registration while disabled. Checked-state storage in the feature/UI is acceptable; adding a `craftableOnly` parameter to catalogue query methods is not.

### Pinned alternatives

An explicitly pinned recipe remains authoritative. If that recipe is unavailable, craftable-only hides the output even if another alternative could be crafted. Filtering must not silently unpin it or substitute another recipe.

The pin is retained while the output is hidden, subject to the existing deselection and category-change behavior. Turning the filter off exposes the pinned alternative again; Auto restores automatic selection. The footer must not offer operations for a hidden row.

## 6. Ordering manager

The proposed `CraftingOrderingManager` wraps a collection of `ICraftingOrderingProvider` instances. Each provider supplies a comparison of two evaluated selections:

```csharp
int Compare(CraftingRecipeSelection left, CraftingRecipeSelection right);
```

- Zero means this criterion does not distinguish the pair.
- Each comparison must be deterministic for the current projection and obey comparer consistency.
- Providers do not modify eligibility, alternatives, or evaluation.
- Registration includes explicit precedence; higher precedence is evaluated first.
- The first nonzero result determines the order.
- Equal precedence is rejected rather than resolved by registration timing.
- A manager-owned ordinal recipe stable-identity comparison is always applied last.

When this manager is introduced, the default chain must reproduce the existing order:

1. Supported placement before unsupported placement.
2. Craftable before noncraftable.
3. Higher relevance first.
4. Fewer missing units first.
5. Localized display name, using the existing culture-aware comparison.
6. Ordinal recipe stable identity.

The availability criteria may be one provider; they do not require one class per comparison field. Name ordering is a separate provider. A future sort feature can replace the active chain atomically rather than adding an ordering-mode branch to the session. Name-only mode need not retain availability as its primary criterion.

Do not introduce a numeric score that combines unrelated criteria or relies on arbitrary weighting.

## 7. Classification manager

Classification is presentation data, not filtering or ordering authority. Initially, the existing availability mapping can remain in place; extracting it solely to populate an unused manager is not required.

When a concrete classification extension needs composition, introduce a `CraftingClassificationManager` around typed classification providers:

- Availability, favorite markers, and recent-placement markers are separate dimensions, not competing values of one universal enum.
- A dimension defines whether contributions are exclusive or additive.
- Exclusive dimensions use explicit provider precedence and select the first applicable contribution; equal precedence within that dimension is rejected.
- Additive dimensions retain compatible contributions using that dimension's defined identity and ordering rules.
- Absence of a contribution is distinct from an authoritative classification such as Unsupported.
- Conflicting or invalid contributions are errors, not an invitation to rely on insertion order.
- Providers supply semantic values; renderers choose colors, textures, and geometry.

A migrated availability classifier must preserve the existing factual mapping:

| Evaluation | Availability classification |
| --- | --- |
| Unsupported, including pending evaluation | Unsupported |
| `CanCraft` | Available |
| Supported, not craftable, with allocated units greater than zero | Partially available |
| Otherwise | Missing ingredients |

An availability provider must not label a recipe craftable contrary to its evaluation. A future cosmetic highlight belongs to its own dimension rather than overriding allocation facts. Preserve the existing tile-status/shader mapping, icon cache, and batch behavior when connecting classification output to rendering.

The concrete typed result shape should be chosen with the first additional consumer. Do not prebuild an untyped property bag or a registry of speculative dimensions.

## 8. Registration, feature ownership, and lifetime

- Managers are owned by one crafting-menu session, not by static or world-global registries.
- Session construction explicitly wires built-in feature contributions; there is no assembly scanning.
- Each registration has a stable, feature-owned identity. Duplicate identities in the same manager are rejected before changing the collection.
- Registration returns an owned removal handle. Releasing it removes only that registration; release is idempotent.
- Criteria replacement and ordering-chain replacement are atomic from the projection's perspective.
- Providers and their criteria are read-only for a projection pass. Registry changes occur on the owning client thread and cannot mutate an in-progress enumeration.
- Feature controllers notify the session that projection state changed through a common refresh path. The session does not inspect provider types or identifiers to determine behavior.
- A criteria-only change reuses current evaluations. An inventory update captures and allocates once, then projects the refreshed selections.
- GUI recomposition binds controls to existing feature state; it does not register duplicate providers.
- Session teardown releases registrations and subscriptions. Recomposition alone does not tear down the session.
- New sessions retain current reset behavior unless a separate persistence feature explicitly restores their state.

Do not silently remove, ignore, or substitute a provider after an exception. Invalid registration and provider failures must be surfaced through explicit errors and the application's existing error-reporting boundary. No failure may be presented as a successful unfiltered or empty query.

## 9. Projection and interaction reconciliation

The session orchestrates the following responsibilities without feature-specific switches:

1. Obtain current evaluated recipe selections, respecting automatic and pinned alternatives.
2. Apply the filter manager.
3. Order eligible candidates using the existing ordering initially, or the ordering manager once introduced.
4. Reconcile candidates with displayed rows when interaction stability applies.
5. Classify the final displayed selections, if classification has been extracted.
6. Publish one coherent row snapshot and update the GUI.

### Stable rows without stale membership

Filtering and ordering have different interaction requirements. Hovering or pressing may defer reordering, but must never defer removal of a row that no longer matches an active filter.

While interaction is active:

- Refresh evaluations for the exact recipes currently displayed.
- Retain still-eligible displayed recipes in their existing relative order, including their shown automatic alternative.
- If a shown automatic alternative becomes ineligible, resolve the current automatic alternative. Replace the row for that output if the replacement is eligible; otherwise remove it.
- Never replace an explicitly pinned alternative merely to pass a filter.
- Append newly eligible output groups in canonical order, without duplicating retained outputs.
- Explicit alternative/Auto actions take effect immediately for their output; interaction stability cannot preserve a recipe contrary to that action.
- After interaction ends, restore canonical ordering and automatic selection.

This behavior applies to every registered filter. There is no branch such as "disable the freeze when craftable-only is enabled."

### Input and layout safety

- Mouse activation must require a matching left-button press for the currently bound output and recipe identities.
- Removal or identity replacement cancels an outstanding press, even if a virtualized tile stays at the same screen position.
- Reintroducing a removed row before release must not revive the canceled press.
- Scrollbar position and internal scroll offset must be clamped together after membership shrinks, including during a held press.
- Removed hover/tooltip targets must be cleared or rebound coherently. Keyboard focus must remain valid; an empty list cannot retain a focusable tile.
- Explicit search, category, and craftable-only changes retain the existing intended return-to-top behavior. Live inventory refresh clamps scroll but does not unconditionally reset it.
- Activation still goes through the existing coordinator's fresh allocation and transfer validation. A provider decision never authorizes a transfer by itself.

These rules address behavior that cannot safely be delegated to pure matching or classification providers.

## 10. Implementation boundaries and sequencing

### First delivery: filter composition and craftable-only

1. Introduce the filter contract, manager, registration ownership, and isolated search/category providers.
2. Route candidate eligibility through that manager while retaining existing alternative resolution and default ordering.
3. Introduce feature-neutral interaction reconciliation and pressed-identity cancellation before enabling live membership-changing filters.
4. Add craftable-only as a feature-owned provider and native toggle. Preserve search-first keyboard tab order, independent category/search state, localized measured layout, and resize state.
5. Add regressions and update user documentation for the delivered behavior.

This delivery does not require the ordering or classification managers.

### Later delivery: ordering contributions

Introduce the ordering manager with the Availability/Name selector or another concrete ranking feature. Migrate the current default order without changing its meaning before enabling an alternative chain.

### Later delivery: classification contributions

Introduce classification composition when a concrete feature needs an additional presentation dimension. Migrate availability presentation with output parity and keep unrelated dimensions independent.

Favorites, Recent, persistence, and ingredient-based relevance remain separate feature work. This proposal establishes their insertion points; it does not define their storage formats or complete UX.

## 11. Verification and acceptance

### Manager and provider tests

- AND composition, no-filter behavior, independent registration removal, and registration-order-independent membership.
- Duplicate registration rejection, atomic replacement, idempotent release, teardown, and protection against mid-pass mutation.
- Existing search normalization and category/Other behavior.
- Full, partial, missing, unsupported, and pending evaluations under craftable-only.
- Pinned unavailable alternatives remain hidden without losing their pins; Auto restores eligible automatic selection.
- Criteria-only updates do not capture inventory or allocate again; inventory refresh performs one allocation pass.
- When ordering is delivered: exact default ordering parity, explicit precedence, tie handling, and comparer consistency.
- When classification is delivered: availability parity, dimension isolation, conflict handling, and no effect on authoritative evaluation.

### Session and native GUI tests

- Category, search, and craftable-only combine independently; removing one restriction preserves the others.
- Inventory changes add and remove eligible rows while hovered, focused, pressed, or previously empty.
- Still-eligible shown alternatives and row ordering remain stable during interaction; canonical projection resumes afterward.
- Replacement within the same output group and replacement by a different output both cancel stale presses.
- Shrinking a scrolled list during a press clamps the viewport and scrollbar without blank or unreachable results.
- Hidden selection does not leave an actionable footer or stale tooltip.
- Native mouse/keyboard toggle operation, search-first focus order, localized minimum sizing, and UI scales 1, 1.5, and 2.
- Resize does not duplicate providers or lose current criteria. A new session starts with the existing default browsing state.
- Existing coordinator validation, native placement behavior, icon caching, and rendering batching remain unchanged.

### Completion criterion

A new filtering feature can contribute its own provider instance without changing the session's filtering or interaction branches, the catalogue query signature, or another filter's implementation. Equivalent insertion-based boundaries apply to ordering and classification when those systems are delivered.

Documentation-only approval of this proposal does not constitute implementation completion or live in-game acceptance.
