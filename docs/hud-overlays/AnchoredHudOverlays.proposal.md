# Anchored HUD overlays

Status: Proposed; awaiting approval. This document defines the design only. An implementation plan will be created after approval.

## Purpose

Provide a reusable client system for adding passive overlays anchored to screen positions or named HUD elements. Adding a feature should require its own content and gameplay logic, a registration, and a placement choice; it should not require another independent positioning, scheduling, or lifecycle system.

The first consumer is an arrow indicator while the player holds a bow. The same placement and lifecycle contracts should accommodate the time, weather, temperature, and temporal-storm indicators listed in [project.todo](../project.todo). Those features' gameplay rules remain separate proposals.

The user has confirmed that both screen positions and named HUD elements are required. The remaining choices below are recommendations for review.

## Existing foundations and evidence

The evidence below combines the current local source checkout with read-only metadata/IL inspection of the installed game assemblies on 2026-10-09. The positioning APIs are present in VintagestoryAPI.dll; client behavior described below was inspected in G:/Vintagestory/VintagestoryLib.dll. Bow selection remains source-backed pending installed verification. No game was launched, and live GUI appearance/input behavior remains unverified.

No base-game IHudOverlay declaration was found in the inspected VintagestoryAPI, VintagestoryLib, VSSurvivalMod, or VSEssentials assemblies. IHudOverlay below is a proposed internal VanillaExpanded contract; the existing native base is HudElement.

| Existing boundary | Verified behavior | Design implication |
| --- | --- | --- |
| [HudElement](../../../vsapi/Client/UI/HudElement.cs) | Extends GuiDialog, identifies itself as HUD, does not prefer an ungrabbed mouse, and applies the native HUD depth translation. | Use a native HUD host rather than introducing an unrelated rendering stage. Passive input behavior still needs explicit overrides. |
| [GuiDialog](../../../vsapi/Client/UI/Dialog/GuiDialog.cs) | Opening registers an unregistered dialog. UnregisterOnClose defaults false. Installed GuiManager.OnGuiClosed removes it from OpenedGuis and, when UnregisterOnClose is true, LoadedGuis. Disposal releases composers. | Open through the native contract; use UnregisterOnClose for host deregistration and close before disposing. |
| GuiDialog.GetFreePos / OccupyPos / FreePos | Public methods reserve predefined EnumPosFlag positions under a shared string key in capi.ObjectCache. GetFreePos finds a free flag but does not reserve it or calculate bounds; it returns zero when exhausted. Present in the installed API. | Reuse this allocator if a feature later needs conventional dialog-position reservation; do not duplicate its occupancy store or treat it as measured HUD packing. |
| [ElementBounds](../../../vsapi/Client/UI/ElementBounds.cs) | Owns parent/child bounds, EnumDialogArea alignment, fixed/percentage sizing, padding, offsets, and relative placement helpers such as RightOf and BelowCopy. Fixed dimensions are scaled; renderX/renderY include render offsets. | Use native root/child bounds and alignment rather than introducing a parallel UI bounds tree. Limit custom geometry to content packing, attachment, viewport fit, and clipping policy. |
| [ICoreClientAPI](../../../vsapi/Client/API/ICoreClientAPI.cs), [IGuiAPI](../../../vsapi/Client/API/IGuiAPI.cs) | Expose GUI hiding, WindowBounds, GUI collections, registration, GetDialogBoundsInArea, and GetDialogPosition/SetDialogPosition. Installed GuiAPI.GetDialogBoundsInArea enumerates opened composers with exactly matching Alignment; saved positions delegate to ClientSettings. | Reuse native window/GUI bounds. An area query is an advisory bounds lookup, not a named HUD selector, collision solver, or layout allocator. Keep overlay preferences in the existing mod configuration. |
| Installed Vintagestory.Client.NoObf.HudHotbar.ComposeGuis | The public inherited Composers collection contains key hotbar; its composer is named inventory-hotbar. Public element keys include hotbargrid, offhandgrid, and backpackgrid. | Resolve the hotbar using the native type and Composers["hotbar"].Bounds. No private-field accessor or render patch is needed for these bounds. |
| [ItemSlotIndicatorSystem](../../VanillaExpanded/src/ItemSlotIndicators/ItemSlotIndicatorSystem.cs) | Owns provider registration and separates sampling from presentation; providers have refresh and configuration contracts. | Retain that separation of responsibilities. Its per-item-slot cache and priority selection are specialized and should not become the HUD overlay registry. |
| [RadialMenuDialog](../../VanillaExpanded/src/RadialMenu/RadialMenuDialog.cs) | Deliberately captures input and renders an interactive menu. | This system needs a separate passive HUD host. Existing radial menus remain independent. |
| [LiveConfigReload](../../VanillaExpanded/src/ModSystems/LiveConfigReload.cs), [VanillaExpandedConfig](../../VanillaExpanded/src/VanillaExpandedConfig.cs) | Provide an existing configuration reload boundary and persisted visual settings. | Extend these boundaries and add a visibly grouped HUD Overlays section to ConfigLib. |
| [ItemBow](../../../vssurvivalmod/Item/ItemBow.cs), GetNextArrow | Finds the first positive arrow stack using EntityAgent.WalkInventory, excludes ItemSlotCreative, and tests the collectible path prefix arrow-. The method is protected and nonvirtual. | Use the owning bow selection method through a narrowly scoped read-only accessor, subject to installed-API verification. Do not introduce a competing sort or preferred-arrow policy. |
| [EntityPlayer](../../../vsapi/Common/Entity/EntityPlayer.cs), WalkInventory | Traverses InventoriesOrdered, skips creative inventories, and includes only inventories opened by the player. | Arrow availability must use this traversal, not a hand-written hotbar/backpack list. Open external inventories can affect the result in this checkout. |
| [InventoryBase](../../../vsapi/Common/Inventory/InventoryBase.cs) | Exposes SlotModified and inventory open/close events. | Coalesce relevant inventory changes into a refresh request; retain a bounded fallback for missed changes. |

## Scope

The shared system owns registration, named anchor resolution, dynamic content packing within anchor groups, update scheduling, global visibility, drawing dispatch, and cleanup. Native ElementBounds and GuiComposer own the actual UI bounds hierarchy, screen alignment, and scaled positioning. Feature modules own applicability, sampled content, content measurement, and drawing their presentation.

The initial design supports any bounded passive visual, including an icon with a number, a text label, or a gauge. An icon-and-text presentation is the first reusable presentation component, not the universal content model. Custom presentations must follow the same measurement, clipping, resource, and input contracts.

Interactive widgets, drag-to-position editing, world/entity projection, automatic discovery of every game's HUD component, automatic avoidance of unrelated UI, and a public cross-mod extension API are outside this proposal. Existing item-slot indicators and radial progress/menu rendering do not migrate as part of this work. The registry can remain internal until an actual external consumer establishes a public compatibility requirement.

## Native positioning reuse

Use one native GuiComposer per overlay group, owned by the passive HUD host, with an ElementBounds root and native child bounds for its participating contents. Stable group IDs identify these compositions. This keeps group bounds available to the engine through the normal GUI surface; a second generic bounds hierarchy is unnecessary.

Screen placements map to the corresponding EnumDialogArea and use WindowBounds as the native window parent. Apply safe insets and group offsets through the native bounds contract. Named HUD attachments compute the desired root origin from the target's rendered rectangle, then express that result through native fixed bounds with Alignment None. Convert pixel results to GUI units at this boundary; ElementBounds performs the final GUI scaling.

The remaining custom layout policy measures current content, selects/order-packs visible members, attaches the group to a target, and handles viewport clamping/overflow. Use native child/relative bounds helpers where their contracts fit; for example, BelowCopy creates a copy rather than an automatically updating vertical stack. Membership or measurement changes therefore require an explicit packing pass. Drawing consumes the resulting native bounds.

GuiDialog's shared reservation methods solve a different placement contract: callers sharing a code reserve one of 18 predefined left/right position flags. Vanilla container dialogs use the code smallblockgui and derive actual coordinates using IsRight, XOffsetMul, and YOffsetMul. The allocator knows neither content dimensions nor hotbar attachments and does not repack when content changes. The initial explicitly anchored groups need no such reservation; they must not occupy smallblockgui positions. If conventional reservations are introduced later, use GetFreePos followed by OccupyPos and release with FreePos rather than adding another occupancy allocator.

GetDialogBoundsInArea can report existing composer bounds for an exact screen-alignment category. Its installed implementation returns an empty list when none match, despite the source documentation mentioning null. It neither identifies which composer is the hotbar nor calculates free space. The initial proposal does not add collision avoidance based on this query.

GetDialogPosition/SetDialogPosition store named dialog positions. The base [GuiElementDialogTitleBar](../../../vsapi/Client/UI/Elements/Impl/Interactive/GuiElementDialogTitleBar.cs) applies those saved positions while implementing draggable windows. These methods are available for a future position editor; the passive overlay system initially uses VanillaExpandedConfig/ConfigLib for its anchor and offset choices and does not add a second persisted position authority.

## Ownership and registration

A client-only HudOverlaySystem is a thin composition root: create the registry, anchor adapters, scheduler, layout service, native host, and built-in registrations; connect lifecycle/configuration events; delegate their work to the owning components.

A registration contains:

| Field | Contract |
| --- | --- |
| Stable ID | Namespaced identity such as vanillaexpanded:bow-ammunition. Duplicate IDs are rejected before changing the registry. |
| Overlay instance | Owns feature content, its current snapshot, measurement, and presentation resources. It exposes refresh, measure, and draw responsibilities through IHudOverlay. |
| Enabled selector | Reads the existing live configuration. Disabled overlays do not sample or occupy layout space. |
| Group ID | Chooses a shared placement group. Each group has exactly one placement and packing policy. |
| Order | Ascending numeric order, then stable ID using ordinal comparison. Registration timing does not change placement. |
| Refresh interval | Validated positive interval in milliseconds. Recommended shared default is 1,000 ms; a feature may request a faster interval. Sampling never runs per frame. |

Groups are registered separately with a stable ID and one placement/packing definition. Reject duplicate group IDs and overlay registrations referencing unknown groups before changing registry state.

Registration returns an idempotent removal handle. Successful registration transfers overlay lifetime ownership to the system; removing it disposes that overlay's resources exactly once. Rejected registrations leave ownership with the caller. Engine-owned icons, atlas resources, item meshes, inventory objects, and API services remain borrowed.

Feature modules register their own overlays. The registry, host, and layout service must never depend on the bow, weather, or temperature modules. No static active instance or global feature singleton is needed for the new system.

Registry mutations requested during iteration are applied at the next pass boundary. All update, measurement, resource preparation, rendering, and removal work occurs on the client main thread.

## Anchors and placement

### Anchor targets

A target resolves to an available, visible rectangle in framebuffer pixels, derived from native bounds. Screen targets use capi.Gui.WindowBounds with a proposed shared safe inset of 12 GUI units. A named HUD target, initially hotbar, resolves through an adapter over the corresponding native GUI and its final rendered bounds. The rectangle is a read-only layout input, not a separate UI bounds model.

Anchor adapters expose only target identity, availability/visibility, and the rectangle. They contain engine integration knowledge; overlay features contain none. Read each required target once per visible host frame and share the result among its groups. Changed rectangles invalidate layout; unchanged rectangles do not require a new layout pass. A hidden, closed, missing, uninitialized, or ambiguous native target is unavailable. Its dependents hide quietly and recover when it becomes available. Do not guess a replacement rectangle from hard-coded hotbar dimensions.

The hotbar adapter finds the installed Vintagestory.Client.NoObf.HudHotbar through capi.Gui.LoadedGuis and reads its inherited public Composers["hotbar"].Bounds after composition. Read renderX/renderY and OuterWidth/OuterHeight rather than the private dialogBounds field. The initial hotbar target means the complete hotbar composer rectangle; hotbargrid, offhandgrid, and backpackgrid are available if a future feature explicitly needs a narrower target. Retain quiet hiding when the native GUI is closed or its composer/bounds are absent.

These type and composer/element keys are verified installed integration points, not a guarantee across every future game version or replacement HUD mod. Preserve the adapter boundary and recheck it when compatibility changes. The actual rendered extent, visibility transitions, and desired docking position still require live acceptance. No private accessor or hotbar rendering patch belongs in the initial adapter.

### Placement model

A placement specifies a target ID, a target attachment point, the group's own pivot, and an X/Y offset in GUI units. Screen choices map to native EnumDialogArea values. Target attachment points and group pivots use the familiar nine positions, represented by normalized coordinates from 0 to 1 only for the attachment calculation. This metadata does not replace native alignment or bounds.

For target rectangle R, measured group size S in pixels, target point A, group pivot P, logical offset O, and GUI scale g:

    groupOrigin = R.origin + A * R.size - P * S + g * O

This equation defines the intended attachment geometry and can validate native screen alignment in geometry checks; screen positioning itself uses ElementBounds.Alignment. Named HUD docking uses the equation to calculate its native root bounds. Positive X is right; positive Y is down. Native target rectangles are already in pixels. Convert computed pixel positions and measured sizes to GUI units before assigning native fixed bounds, allowing ElementBounds to scale exactly once. Do not assign already-scaled positions as fixedX/fixedY.

Proposed bow placement: group held-item-status attached to the hotbar's right-middle, with its own left-middle pivot and an offset of (12, 0) GUI units. The exact visual placement is subject to user-run in-game review. A screen placement can be selected explicitly in configuration if that location is preferred or the hotbar adapter is unavailable; missing-target behavior itself remains hide, with no automatic relocation.

### Group layout

Each group owns horizontal or vertical packing, gap, padding, and cross-axis alignment. The held-item-status group initially uses vertical packing. Only applicable, enabled overlays with available anchors participate. Hiding an overlay releases its space; remaining entries retain their relative order.

Measure all participating contents, update the group's native root size, position it through native screen alignment or named-target attachment, then assign native child bounds in deterministic order. Clamp the whole group to the viewport safe rectangle, preserving member spacing. This may move a group away from its preferred attachment near a screen edge; it must not independently clamp members into overlap.

If the group cannot fit, retain the longest ordered prefix that fits. Omitted entries remain eligible for updates and reappear when space permits. A single oversized first entry is clipped to the safe rectangle rather than scaled unreadably. Presentations must support the host's clip contract; text presentation can ellipsize within its assigned rectangle. Overflow behavior is deterministic and must not repeatedly recompose content every frame.

Two groups can deliberately overlap; this system only prevents overlap among members of the same group. The host does not promise collision avoidance with chat, maps, status bars, dialogs, or other mods. Configured group placement is the initial means of resolving those conflicts.

## Update, visibility, and drawing

### Updates

A shared scheduler uses monotonic time and a proposed 100 ms client heartbeat. It samples an overlay when its content is invalidated or its refresh interval expires, coalescing multiple invalidations into one refresh. Cheap applicability checks may run on that heartbeat even while feature content is hidden; expensive sampling requires applicability and an available anchor.

The overlay publishes its current stable snapshot on refresh. Drawing reads that snapshot and prepared resources without inventory scans or gameplay queries. A content change marks presentation preparation dirty; a size change marks its group's measurement/layout dirty. Position-only changes do not rebuild text textures or item meshes.

Disabled features stop sampling immediately on configuration reload. A newly enabled feature, changed player/session, newly available anchor, or restored visible HUD receives a fresh sample before displaying previous content. While HideGuis is true, defer expensive sampling and preparation; retain invalidation so returning to the HUD refreshes it. Overflow suppression does not change feature applicability or stop its bounded refreshes.

No adaptive scheduler is needed initially. The bow overlay requests a 250 ms fallback refresh while applicable and marks itself dirty on supported inventory changes. The heartbeat adds up to 100 ms of scheduling granularity to a requested interval; event invalidation is serviced on the next heartbeat. These are proposed scheduling bounds, not measured performance claims.

### Visibility and input

The native host derives from HudElement, opens without focus, reports Focusable false, declines keyboard and mouse events, and captures neither general nor raw input. Overlays have no input handlers. Bow drawing, movement, scrolling the hotbar, inventory interaction, and Escape must retain their normal behavior.

All overlays hide when the world/player is unavailable, during world exit, or when HideGuis is true. Feature applicability can add restrictions; the initial bow overlay hides while the player is dead. Ordinary inventory dialogs do not automatically hide overlays. Modal menus cover the overlays through normal GUI ordering; the exact installed ordering must be verified.

Use the normal HUD draw band below inventory dialogs, tooltips, and modal menus. Select the concrete DrawOrder from installed ordering evidence rather than copying the radial menu's topmost order. Preserve native HUD transforms and restore any temporary rendering state through the established engine APIs. Do not add shaders or custom OpenGL state machinery for the icon-and-text indicator.

### Resource lifecycle

The registry owns registration lifetime; the host owns drawing dispatch and its own GUI resources. Each overlay owns its feature resources, including text textures; shared presentation helpers own any resources they cache. Prepare on content, locale, font, or GUI-scale changes, and reuse otherwise. Draw item icons through the game's GUI item rendering path using a presentation copy of the selected stack; the copy must not allow rendering to mutate live inventory state.

Viewport size, GUI scale, changed native anchor bounds, membership, order, placement configuration, and measured content size invalidate layout. Reading a live anchor rectangle is cheap; unchanged rectangles do not trigger recomposition.

Set the host's UnregisterOnClose to true. In the inspected installed client, GuiManager.OnGuiClosed removes the host from OpenedGuis and removes it from LoadedGuis when that flag is true. Close through TryClose before disposing; do not edit native GUI collections directly. Reopening uses TryOpen's normal registration contract.

On LeaveWorld, stop updates, detach feature subscriptions, close and dispose the session host and its group composers, and release session-bound snapshots/resources and native-target references. Retain registration descriptions for rebinding, not live disposed presentation resources. On the next ready world, create a new host and fresh session bindings/resources. On final disposal, detach remaining events/listeners and release registrations and host resources idempotently. Implementation validation must confirm cleanup for repeated open/close, disable/re-enable, and world exit; composer disposal alone is not deregistration.

A failed feature refresh or preparation hides that overlay and emits one bounded diagnostic for the failure, without taking down the host. Retry at its next scheduled refresh, avoiding per-frame log spam. Missing optional anchors are an ordinary visibility state and emit no warnings.

## First consumer: bow ammunition

### Meaning of the display

The indicator shows the arrow type the supported bow would select next and the total positive quantity of that collectible type in the same eligible inventory traversal. It does not present a selected ammunition mode or a reserved arrow: the inspected bow selects again on release.

For example, if the first eligible arrow is flint and the traversal contains stacks of 12 and 8 flint arrows plus 30 copper arrows, display a flint arrow icon and 20. After those flint arrows are exhausted, display the next type chosen by the bow and its quantity.

Quantity is grouped by collectible identity, not stack attributes, because the inspected selector tests the arrow path prefix and positive quantity rather than stack equality. Use a sufficiently wide accumulator and never include another arrow type. Use localized item names and accessible contrast; proposed normal presentation is an item icon plus count, with no always-visible full item name. With a supported bow and no eligible arrows, display a muted generic arrow symbol and localized No arrows/0 state. With no supported bow in the active main hand, hide and release layout space.

### Selection authority and compatibility

Bow-specific code resolves the next slot by invoking the existing protected GetNextArrow through a verified cached read-only accessor, using the mod's existing Harmony dependency if needed. Do not patch firing, reserve ammunition, move stacks, change inventory, or introduce a selection preference. Cache the method binding, not the selected live slot indefinitely.

Count matching positive stacks using the same EntityPlayer.WalkInventory traversal and creative-slot exclusion. In this checkout, an opened external inventory can participate. Preserve that behavior if installed verification confirms it; do not silently narrow the display to personal bags when that differs from firing selection.

Initially support the verified base ItemBow behavior. A derived or custom bow with unverified firing/selection semantics requires an explicit feature-specific adapter; otherwise hide the indicator for it. Generic bow tags or EnumTool.Bow alone cannot establish ammunition selection compatibility. This compatibility policy does not belong in the shared overlay host.

Inventory subscriptions must include all inventories that can affect the traversal, not only the currently selected stack. Attach/detach SlotModified and open/close callbacks as that inventory set changes. The bounded fallback refresh also rechecks inventory membership and active-hand identity. The initial implementation must verify which installed events can shorten that fallback without pretending all mutations are guaranteed to emit them.

The display is based on current client-visible inventory state. Server-authoritative firing and synchronized changes may briefly differ; refresh on those changes rather than predict decrements locally. Once the player switches away from the bow or the session changes, discard the prior arrow snapshot.

## Proposed source responsibilities

Each named component owns one responsibility; exact filenames can follow established project conventions during implementation.

| Location | Responsibilities |
| --- | --- |
| src/HudOverlays/HudOverlaySystem.cs | Client composition and lifecycle/event wiring only. |
| src/HudOverlays/Registration/ | IHudOverlay contract, metadata, stable registry, and removal lifetime. |
| src/HudOverlays/Anchoring/ | Placement metadata mapped to native alignment, named target contract, and public hotbar composer adapter. |
| src/HudOverlays/Layout/ | Measurement/order/packing and viewport-fit policy applied to native bounds; pure geometry only for attachment, fit, and clipping calculations. |
| src/HudOverlays/Updating/ | Shared scheduling, invalidation, and applicability/visibility transitions. |
| src/HudOverlays/Rendering/ | Native passive host, group composers/child bounds, and reusable icon-and-text presentation. |
| src/BowAmmunition/ | Bow applicability, authoritative selector access, inventory sampling/subscriptions, and overlay content. |

Keep the initial contracts internal and small. Do not introduce a generalized widget tree, service locator, generic event bus, or item-slot cache refactor. Classes and methods require XML documentation, nontrivial logic requires explanatory comments, and method regions follow the supplied visibility/family convention.

## Configuration

Extend VanillaExpandedConfig and the existing ConfigLib mapping/localization with a HUD Overlays section. Proposed initial controls are EnableHudOverlays and EnableBowAmmunitionOverlay (both default true), and held-item-status placement: named hotbar or one of nine screen positions, plus X/Y offsets. Group direction, gap, padding, and overlay order are registration defaults initially.

Settings are local presentation preferences and apply live through ILiveConfigurable. Disabling one feature removes only its content and space; disabling the system stops expensive overlay work while retaining the ability to re-enable it without restarting. Unknown anchor keys fall back to the group's documented default; nonfinite offsets fall back to defaults. The configuration parser and ConfigLib display must agree on persisted keys and defaults.

## Validation required before implementation is considered complete

- Recheck the installed contracts at implementation time: the public hotbar type/composer/element keys and native positioning/close APIs are already inspected; draw ordering, live visibility/extent, and GetNextArrow/WalkInventory selection agreement remain prerequisites. If a supported game version differs, reconcile this proposal before relying on a replacement policy.
- Geometry and native-bounds integration checks cover the nine EnumDialogArea screen mappings, named attachment points, differing pivots, GUI scaling once at the native boundary, parent/child bounds, render offsets, ordered packing, hiding/reappearance, resize, group clamping, and deterministic overflow. Confirm group composers expose the resulting bounds without a parallel layout authority.
- Scheduler/lifecycle checks cover coalesced invalidation, refresh bounds, no sampling for disabled/inapplicable or unavailable-anchor content, restoration after HUD hiding, registry removal, and world transitions. Drawing must not trigger gameplay sampling or resource recreation for unchanged content.
- Bow checks cover multiple stacks/types, selector order, zero/empty stacks, creative exclusions, traversal membership/open inventories, unsupported bows, no arrows, hotbar switches, synchronized consumption, and player/session replacement. Ensure every borrowed resource and event subscription has an owning cleanup boundary.
- Follow the user's instruction to run build/test tools through subagents when implementation validation is authorized. This proposal does not require a build or test run.
- Complete a second review and independent implementation audit before checking completion markers. Automated evidence cannot establish live appearance, input pass-through, or gameplay selection agreement.
- User-run in-game acceptance covers normal and high GUI scale, window resizing, hotbar locations, HUD hiding, dialogs/tooltips, bow drawing and release, arrow-type transitions, world exit/rejoin, and representative third-party HUD layouts. Do not launch the game on the user's behalf.

## Approval decisions and remaining evidence

Approval is requested for the shared host/group architecture, quiet hiding for unavailable named anchors, explicit screen-placement alternatives, deterministic overflow, icon-plus-count presentation, type-total quantity semantics, and the base-bow compatibility boundary.

Native positioning APIs and the public hotbar composer access are verified source/installed-assembly foundations. Group packing, attachment policy, and overlay lifecycle remain proposed behavior. Installed bow-selection agreement, final draw ordering, live hotbar extent/visibility, the proposed hotbar-right placement, and responsiveness intervals still require their stated verification and visual/runtime acceptance. After this proposal is approved, derive a separate dependency-ordered implementation checklist with traceability to these contracts. Keep the existing project feature entries unchecked until implementation and acceptance are complete.
