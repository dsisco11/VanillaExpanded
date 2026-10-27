# Native GUI elements for HUD indicators

Status: Approved by the user in conversation. Design authority for the [implementation plan](GuiElementIndicators.todo); this document does not claim runtime implementation.

## Problem

The host currently registers one `HudOverlayGroupElement` per group. That element dispatches rendering to `IHudOverlay.Draw`; individual indicators and their components are not composer-managed GUI elements. The recent native-bounds refactor still resolves component layout from drawing and backdrop collection. This leaves a parallel presentation lifecycle and duplicated layout responsibilities.

## Proposed design

Retain one native `GuiComposer` per effective HUD container, owned by the existing `HudElement` host. Register actual indicator presentation elements with that composer, replacing the group-wide drawing dispatcher.

- Feature controllers retain applicability, gameplay sampling, refresh scheduling, and stable presentation data. They do not inherit `GuiElement` merely to participate in sampling.
- Each presentation supplies real `GuiElement` instances and parented `ElementBounds`. Use native text elements where their update, contrast, and ownership contracts fit. Implement small custom elements for item icons, layered weather, thermometer masking, and animated instruments.
- Icon and reading components participate directly in native composition. Wrapping the existing entire `Draw` implementation in one callback element without transferring its component layout and resource lifecycle does not satisfy this proposal.
- Keep application-specific group packing, anchors, viewport clipping, and saved arrangements in their current owners. Native bounds own component alignment and scaling; do not introduce another widget tree or general layout framework.

## Update and rendering contract

Content preparation updates native text or custom element state and determines whether measurements changed. The host then packs visible indicators, assigns their native bounds, and performs the necessary native bounds calculation or recomposition before either rendering pass.

Membership, style, measured size, GUI scale, viewport changes, and movement invalidate the relevant composition or bounds. Position-only changes update bounds without rebuilding unchanged text or textures. Same-size numeric updates use the element's content-update mechanism. Deriving from `GuiElement` does not itself provide automatic content measurement or invalidation; those responsibilities must be wired explicitly.

Rendering consumes prepared resources and resolved bounds. No component measurement, text fitting, stack configuration, or `CalcWorldBounds` calls occur in foreground or backdrop rendering. Animation may update its visual effect without rearranging components. Backdrops, clipping, fades, and foreground artwork retain their established ordering and opacity behavior.

Retire `HudIndicatorStackLayout` and manual per-component absolute-bounds assignment as native element compositions take over their responsibilities.

## Ownership and compatibility

Composers own their elements; elements own their generated GUI resources. Engine-cached textures, inventory objects, and API services remain borrowed. Each element belongs to one composer. Regrouping and drag previews must not share a disposable element between composers or duplicate gameplay controllers and subscriptions. Style changes, completed exits, world changes, and disposal must release each owned resource exactly once.

Preserve indicator IDs, settings, saved layouts and locks, grouping/editing, hidden-item proxies, passive input behavior, current sizes, and text-above-icon ordering. Add a native-presentation capability alongside the controller registration boundary. All indicators migrate to native presentations. There are no external consumers, so no compatibility adapter or external source/binary compatibility obligation is required.

## Acceptance

Verify native element registration, change-driven composition, and zero layout work during repeated rendering. Cover text updates, scaling, optional components, backdrop alignment, animation, dragging/regrouping, preview cancellation, controller ownership, and resource cleanup. Run appropriate tests and the canonical package build. User-run in-game review establishes appearance and input behavior.

Approval covers this presentation refactor. A dependency-ordered implementation checklist follows only after approval.

## Source boundaries

- [Current host](../../VanillaExpanded/src/HudOverlays/Rendering/HudOverlayHost.cs) and [group dispatcher](../../VanillaExpanded/src/HudOverlays/Rendering/HudOverlayGroupElement.cs).
- [Public overlay contract](../../VanillaExpanded/src/HudOverlays/Registration/IHudOverlay.cs).
- Native [GuiComposer](../../../vsapi/Client/UI/GuiComposer.cs), [GuiElement](../../../vsapi/Client/UI/Elements/Impl/GuiElement.cs), and [ElementBounds](../../../vsapi/Client/UI/ElementBounds.cs).
