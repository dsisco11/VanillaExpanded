# Anchored HUD overlays

The HUD overlay system provides shared positioning, scheduling, and rendering for passive information displays. Features supply their own content and gameplay rules; the shared runtime places that content on the screen or relative to an existing HUD element. Overlays do not receive input.

## Architecture

An overlay belongs to a named group. The group defines its anchor, attachment point, offset, and packing rules. The runtime measures eligible overlays, packs them in a stable order, and positions the group using the game's native bounds system. Each group renders through a native GUI composer owned by the HUD host.

The main responsibilities are:

- **Registration:** tracks overlay identities, group definitions, and lifetime ownership.
- **Anchoring:** resolves screen bounds or the rendered bounds of a named HUD element.
- **Scheduling:** refreshes feature content at bounded intervals and combines event invalidations.
- **Layout:** packs measured content, positions groups, and handles viewport limits.
- **Rendering:** draws prepared content through a passive native HUD host.
- **Session lifecycle:** binds overlays to the current world and player, and releases session resources when that context ends.

`HudOverlaySystem` connects these components to the client lifecycle and configuration reloads. Feature modules depend on the shared contracts; the shared components do not depend on individual features.

## Content and presentation

`IHudOverlay` separates gameplay sampling from presentation. Applicability determines whether the feature should participate. Refreshing captures the current content; preparation creates or updates presentation resources; measurement supplies the dimensions used by layout. Drawing consumes that prepared state without querying inventory or other gameplay state.

This separation allows placement changes to reuse existing content and textures. Features can invalidate their content in response to game events, while periodic refreshes cover changes for which reliable notifications are unavailable. A failed refresh or preparation hides the affected overlay and permits a later retry without interrupting other overlays.

## Placement and visibility

Anchors expose rendered rectangles rather than owning another UI hierarchy. Native `ElementBounds` controls alignment and GUI scaling. The layout component adds group packing, attachment, viewport clamping, and clipping policy.

A missing or hidden anchor makes its dependent group unavailable. The runtime does not invent fallback coordinates or relocate the group automatically. Disabled or inapplicable overlays release their layout space. When visibility returns, content is refreshed before it becomes drawable.

Placement preferences come from the existing mod configuration. Configuration reloads update group metadata and visibility; they do not create a second position store or rebuild presentation merely because a group moved. User settings and defaults are documented in the [README](../../README.md#hud-overlays).

## Ownership and lifecycle

Successful registration transfers ownership of the overlay instance to the registry. The removal handle ends that ownership. A registered instance can survive multiple world sessions, so session teardown and final disposal are separate responsibilities.

Each feature owns its subscriptions, sampled content, and presentation resources. Session teardown releases those resources and drops references to the previous world and player. The host owns group composers. Engine services, inventory objects, meshes, and atlas resources are borrowed and must not be disposed by an overlay.

## Adding a feature

Implement `IHudOverlay` in the feature module and register it with a stable ID, an enabled selector, a group, an order, and a refresh interval. Keep applicability and sampling in that module. Use the shared icon-and-text presentation when it fits the content, or provide a bounded passive presentation that follows the same preparation and clipping contract.

Subscribe to relevant game events during the feature's active lifecycle and use them to request invalidation. Release subscriptions and owned resources when the feature is suspended or its session ends. Add an anchor adapter only when a feature needs a native HUD target that the system does not already expose.

## Bow ammunition

The bow indicator is the first consumer. Its feature module determines bow compatibility, obtains the next arrow through the base bow's selection method, and counts matching arrows using the same eligible inventory traversal. The shared runtime only sees measured, prepared overlay content.

Item presentation reuses the quick-swap menus’ shared renderer and asset-authored `ve-radial-menu-properties`. Arrow patches frame the working end independently of inventory transforms. The presentation clips the enlarged model to its icon slot, keeping it separate from the count. Missing or unsupported custom presentation falls back to the engine icon path.

Selection policy remains with the game. The indicator neither changes ammunition order nor reserves or consumes arrows. Custom bows require a feature-specific adapter that establishes their selection behavior.
