# HUD overlay integration

The HUD overlay system positions and updates passive displays on the client. An overlay supplies content and a measured footprint; the runtime handles anchoring, group layout, refresh scheduling, GUI scaling, and world-session transitions.

Reference `VanillaExpanded.dll` and the compatible Vintage Story API assemblies, and declare `vanillaexpanded` as a dependency in your mod metadata. `HudOverlaySystem`, `IHudOverlay`, registration and placement contracts, and the shared icon-and-text presentation are public and can be used from a separate mod assembly. Obtain the running system through the client mod loader; the game owns its lifetime.

## Registering an overlay

Implement `IHudOverlay` and register an instance through `HudOverlaySystem.Registry` during client startup, after the HUD system has registered its groups. Existing consumers use `ExecuteOrder() => 1` for this ordering.

A `HudOverlayRegistration` specifies:

- A unique namespaced ID, such as `example:status`.
- The overlay instance and a cheap enabled selector that reads the feature's current setting.
- An existing group ID.
- An integer display order and a positive refresh interval in milliseconds.
- Optional localization metadata for a hover title and a fixed footprint for a reserved slot.

The registry rejects duplicate IDs, unknown groups, and instances it already owns or has disposed. Successful registration transfers ownership of the overlay to the registry. Keep the returned `IDisposable` handle and dispose it when removing the integration. If registration fails, the caller still owns the instance and must clean it up.

For example, use this fragment in your client mod system, where `ExampleOverlay` implements `IHudOverlay` and `featureEnabled` is your setting:

```csharp
var hud = api.ModLoader.GetModSystem<HudOverlaySystem>();
var overlay = new ExampleOverlay();
IDisposable registration;
try
{
    registration = hud.Registry.Register(new HudOverlayRegistration(
        id: "example:status",
        overlay: overlay,
        isEnabled: () => featureEnabled,
        groupId: "player-status",
        order: 50,
        refreshIntervalMs: 1000,
        hoverLabelKey: "example:hud-status",
        reservedSize: new System.Drawing.SizeF(24, 24)));
}
catch
{
    overlay.Dispose();
    throw;
}
// Retain registration and dispose it when the integration is removed.
```

The fragment uses `VanillaExpanded.HudOverlays` and `VanillaExpanded.HudOverlays.Registration`. Placement types are in `.Anchoring`, packing types in `.Layout`, and `HudOverlayIconTextPresentation` in `.Rendering`. Registration and overlay callbacks run on the client main thread. Dispose your registration handle when unloading; the shared system and registry remain owned by VanillaExpanded.

## Implementing the lifecycle

A registered overlay may survive several world sessions. Keep session resources separate from the registration's lifetime.

| Method | Integration responsibility |
| --- | --- |
| `BeginSession(api)` | Bind the current client world and subscribe to relevant events. |
| `IsApplicable()` | Cheaply decide whether content can currently be sampled. |
| `Refresh()` | Capture a stable snapshot and report what changed. |
| `Prepare(context)` | Create or update presentation resources for that snapshot. |
| `Measure()` | Return finite, nonnegative `SizeF` dimensions in unscaled GUI units. |
| `Draw(renderer, bounds, clip, deltaTime)` | Render prepared content within the supplied bounds and clip. |
| `EndSession()` | Detach events, release owned session resources, and clear world references. Make this safe after partial initialization and repeated calls. |
| `Dispose()` | Perform final cleanup when the registration is removed. |

The registry calls `EndSession` before final disposal. Dispose resources allocated and owned by the overlay; retain the engine's ownership of borrowed services and cached assets. In particular, dropping a reference to an engine-cached texture does not require disposing that texture.

## Refreshing and drawing

`Refresh` returns `HudOverlayChange.None` for unchanged content, `Presentation` when visual resources need updating, and `Measurement` when the footprint must be measured again. Combine the flags when both change. The preparation context supplies the client API, GUI scale, locale, and font revision so presentation can adapt when those values change.

Periodic refreshes provide a fallback for data without reliable change events. For event-driven updates, call `hud.Invalidate(overlayId)` on the client main thread. Invalidation is coalesced into scheduled work; it does not refresh immediately inside the event callback. Keep subscriptions and gameplay sampling in the integration.

Drawing consumes the prepared snapshot. Avoid gameplay queries and resource creation in `Draw`. The supplied `clip` is in framebuffer pixels, while measurement and placement offsets use unscaled GUI units. Honor the clip for oversized content and restore any temporary render state. The runtime retries failed refresh or preparation work without interrupting other overlays.

## Choosing placement

An overlay belongs to a group. A `HudOverlayGroup` defines one placement and a `HudOverlayPacking` policy for its members. Register a group before registering its overlays; use a distinct group ID when the integration needs independent placement. `UpdateGroup` changes an existing group's placement or packing without replacing its overlay instances.

`HudOverlayPlacement` defines the target, the attachment point on that target, the pivot on the group, and X/Y offsets. Available target IDs are `screen`, `hotbar`, and `saturation`. Attachment and pivot values come from `HudOverlayPoint`. For example, a target's `LeftBottom` attachment with a group's `RightBottom` pivot places the group to the target's left, aligned at the bottom, before offsets and viewport clamping.

Packing controls horizontal or vertical flow, spacing, padding, and cross-axis alignment. Members are ordered by ascending `Order`, then by ordinal overlay ID.

For a stable slot, supply a positive `reservedSize` in unscaled GUI units when registering the overlay. The layout uses this footprint from the start, including while the overlay is disabled, inapplicable, empty, or recovering from an error. Drawing still requires successfully prepared, visible content; a reservation alone exposes no hover target. Choose a footprint that accommodates the presentation's largest intended content and keep artwork at stable coordinates within the supplied bounds. Content exceeding the slot is clipped. Registrations without a reservation continue to use their measured size and release space when hidden.

The built-in groups are:

| Group | Intended use |
| --- | --- |
| `player-status` | Horizontal status icons beside the hotbar's bottom-left edge. The time indicator uses order 100; use a lower order to appear before it. |
| `held-item-status` | Held-item information using the user's configured anchor and offsets. |

Joining a shared group adopts its placement and packing rules. Additional native HUD targets currently require extending the anchor resolver; there is no external anchor-registration API.

## Visibility and cleanup

The global HUD setting, the registration's enabled selector, applicability, and anchor availability determine visibility. A missing or hidden anchor hides its group without choosing a fallback location. The built-in indicators use their measured sizes and collapse when hidden, disabled, inapplicable, or empty. The remaining indicators close the gaps while preserving their order. Custom registrations can opt into a fixed reservation as described above.

An applicable overlay can return an empty footprint when it has nothing to show. This keeps it eligible for refreshes that may reveal content later. When visibility returns, the runtime refreshes content before drawing it.

Disabling a registration does not remove it or end its session. If disabling a feature should immediately release its resources, the integration must perform that cleanup as part of its own configuration handling. Existing integrations also invalidate their registration and call `hud.ConfigurationChanged()` to update visibility promptly.

Set the registration's optional `hoverLabelKey` to a namespaced localization key explaining what the indicator represents. The shared host resolves the label in the current language and displays a native tooltip when the mouse is free and over the indicator's visible footprint. Hidden, empty, clipped-out, or unavailable indicators expose no hover target.

Hover labels do not capture input or take focus. Overlays receive no mouse events or hover callbacks; interactive controls require separate API support.
