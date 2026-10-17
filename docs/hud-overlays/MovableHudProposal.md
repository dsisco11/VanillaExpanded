# Movable HUD indicators

## Recommendation

Reuse Vintage Story's HUD/dialog lifecycle, GUI event routing, native bounds and scaling, and native dialog-position persistence. Prefer the stock titlebar's drag behavior where it can be adapted to hover controls; extend the existing VE layout runtime for individual placement and player-created groups formed by snapping. Individual feature overlays and their presentation classes should not implement dragging or persistence.

This proposal describes the complete intended interaction. The current implementation provides native persisted movement controls for all registered groups while preserving their internal packing. Individual movement and snapping are not yet implemented.

Implementation checklist: [MovableHud.todo](MovableHud.todo).

## Base-game findings

The investigation used the local API source and the installed Vintage Story 1.22.7 client assembly.

| Facility | Verified behavior | Reuse |
| --- | --- | --- |
| `HudElement` / `GuiDialog` | HUDs are native dialogs. `HudElement` does not request an ungrabbed mouse. Mouse delivery and input capture are separately overridable. | Keep the existing native HUD host; enable narrowly scoped pointer interaction. |
| `GuiComposer` / `GuiElement` | Composers dispatch mouse down, move, and up to interactive elements. Bounds support hit testing and native parent/child geometry. | Implement the handle as a native GUI element, using the existing compositions. |
| `GuiElementDialogTitleBar` | Its Fixed/Movable menu enables dragging. Mouse movement updates parent `fixedX/Y` using pixel delta divided by GUI scale. Release stores the position. | Prefer native dragging, with an adapter for hover visibility and snapping. |
| `IGuiAPI.GetDialogPosition` / `SetDialogPosition` | Installed `GuiAPI` delegates directly to global `ClientSettings`. | Use as the position authority, with stable VE namespaced keys. Reuse coordinates; arbitrary membership needs additional metadata. |
| `GetDialogBoundsInArea` | Installed implementation returns open composer bounds whose `Alignment` equals the requested dialog area. | Bounds discovery only; this is not a snapping or docking service. |
| `GuiDialog.GetFreePos` / `OccupyPos` / `FreePos` | Reserves predefined flag-based window positions. | Not appropriate for arbitrary player placement or edge alignment. |

No general snapping or docking facility was found in the inspected native GUI API and window-drag path. Stock dragging changes positions freely; it does not calculate neighboring edges or snap relationships.

The stock titlebar already supplies the essential dragging and persistence. Its direct use of native client settings is suitable. It is not a complete drop-in solution for the proposed hover-only handles: its movable/moving state and setup methods are private, it includes close/menu controls, and it exposes no public drag-completed callback. Prefer a narrow adapter around native event handling and parent bounds, with snapping applied before release stores the position. Hover rendering, small indicator dimensions, and cancellation need validation before claiming that the widget can be reused unchanged. Retain native titlebar and dropdown machinery through the compact skin; native coordinate storage remains reusable; membership requires additional metadata.

Native `GuiManager` dispatches mouse events through registered dialogs until one handles them. A handled mouse-down also invokes its focus-management path, which can unfocus another dialog even if the HUD itself is non-focusable. Interaction must respect foreground windows and must be checked with inventories and other dialogs open; setting `Focusable = false` alone does not guarantee that other dialog focus remains untouched.

Source references: [titlebar](../../../vsapi/Client/UI/Elements/Impl/Interactive/GuiElementDialogTitleBar.cs), [HUD base](../../../vsapi/Client/UI/HudElement.cs), [dialog input](../../../vsapi/Client/UI/Dialog/GuiDialog.cs), [composer](../../../vsapi/Client/UI/GuiComposer.cs), [bounds](../../../vsapi/Client/UI/ElementBounds.cs), [GUI API](../../../vsapi/Client/API/IGuiAPI.cs). Installed `GuiManager` and `GuiAPI` implementations were also inspected to verify dispatch and storage behavior.

## Player interaction

- Show handles only while the mouse is ungrabbed, the HUD is visible, and no foreground dialog owns that pointer location. Normal gameplay keeps the existing passive behavior.
- Hovering an indicator reveals its title and a small drag grip. A separate group grip identifies the containing group, so moving an indicator and moving its group are distinct actions.
- Handles are transient overlays. They do not enlarge measured indicator footprints, shift neighboring indicators, or trigger content preparation. Place them above the target when space permits and below it at the screen edge. Keep the handle visible while the pointer travels from the indicator to it, and throughout dragging.
- Left-drag a grip to move its target. Consume pointer events only for a grip interaction or active drag; leave clicks elsewhere unchanged. Continue the drag outside the original bounds until release.
- A group drag moves its currently grouped members together and preserves their packing. Dragging an individual indicator away detaches it from automatic packing and gives it its own placement. Remaining members close the gap.
- Dropping an individual onto a grouping snap creates a group with an independent target, or adds the item to the target group. Dragging an individual away removes it from that group. Group controls move the current members together. Provide ungroup and restore-default actions, plus reset-all. Custom groups need stable internal IDs, not predefined names.
- Escape cancels an active drag and restores its starting placement. Mouse capture changes, world exit, GUI hiding, target removal, or loss of the active interaction cancel safely rather than saving a partial drag.
- Hidden/inapplicable indicators retain their saved placements but do not expose invisible drag targets. They can be moved when visible; an editor for hidden indicators would be a separate optional extension.

### Hover control composition

Use the existing hover targeting to find a drawable indicator under a free cursor. Its titlebar and the containing group's grip are distinct targets, with an explicit target identity for each interaction.

Place the controls in a separate native control composition so their visibility never changes the measured content footprint or group packing. The control composition follows the target's effective bounds. Keep it visible while the pointer moves from the indicator onto the controls and throughout an active drag; hide it when neither region is hovered and no interaction remains active.

Prototype a narrow adapter around `GuiElementDialogTitleBar` that forwards native mouse events, gates rendering and hit testing to the hover/drag state, uses a stable dialog name for persistence, and observes the resulting parent bounds for layout updates and snapping. The control composition's origin and the persisted content origin must use a consistent mapping, including when the titlebar appears below an indicator near a screen edge.

The adapter uses `GuiElementDialogTitleBar` with its actual native Fixed/Movable dropdown. Its titlebar skin is compact: 22 GUI units high, standard 14-point HUD text, and the radial menu's orange hover background. Native menu and close glyphs and their hit rectangles follow the slimmer geometry. Retain the native dropdown interaction rather than a direct mode-toggle button. Keep the proven cancellable content-drag integration around the widget so cancellation does not commit a position.

The group control implementation composes the native titlebar with this compact skin and uses its own `GuiElementListMenu`, including native menu selection callbacks. A small adapter isolates access to the titlebar's private menu/icon bounds and suppresses its first-render parent-position write: the temporary hover titlebar position is not the saved content position. Content dragging and cancellation remain owned by the shared movement integration rather than the titlebar's private drag state. Native initialization, menu resources, and dropdown behavior are reused without patching the engine. Native source/headless contract checks and user acceptance remain separate evidence requirements.

Show a partly transparent orange panel behind the exact group bounds while hovered or interacting. While the cursor is free and the group is Movable, also show an unfilled orange line rectangle around those bounds, including when the pointer is elsewhere. Use the radial menu's orange palette for both. Keep these decorations outside measurements and placement calculations. Foreground windows suppress hover fill and controls at their pointer locations, but the movable outline remains a global free-cursor cue. Hide it for hidden HUDs, a grabbed cursor, unavailable content, or modal input capture.

## Placement and group behavior

Groups are runtime containers formed by player actions, rather than fixed named destinations. Any registered indicator can join another indicator's group. Stable feature identities retain sampling, visibility, and resource ownership; built-in group definitions supply initial/reset arrangements only. Custom groups use opaque stable IDs and need no user-assigned name.

Resolve membership and placement before packing, without rewriting feature registrations or duplicating overlays. Layout and scheduler availability consume the same effective container. Independent and user-positioned containers use screen availability even when their former native anchor disappears. Preserve native scaling, integer content origins, and effective screen clamping.

Individual dragging previews removal from the source group, closing its gap. A successful grouping drop creates or joins a target group; a free drop leaves the item independent. Cancellation restores starting membership, order, identity, and placement without writes. Group dragging preserves membership. Hidden/inapplicable members retain membership but expose no invisible targets and collapse through normal visibility rules.

Each item belongs to exactly one container. Remove empty groups; normalize singleton groups into independent items without moving their displayed content. Preserve metadata for temporarily unavailable indicator IDs. Repack after visibility/size/style changes while retaining container origin, subject to reachability.

### Proposed interaction defaults and decisions

- Adjacent-edge snapping to an independent item creates a group; snapping to a grouped item inserts into its existing group. Do not create nested groups.
- Highlight the destination, insertion side/order, and resulting layout before release. Only the selected grouping candidate changes membership.
- Screen-edge and matching-edge/center guides remain alignment aids. Shift bypasses snapping and grouping, permitting independent nearby placement.
- Group dragging keeps its members together. Decide separately whether whole-group merging should be supported; it is not implied by individual insertion.
- Keep Fixed/Movable movement permission distinct from ungroup and restore-default operations. Do not let a native Fixed callback silently erase custom membership.

Before implementing geometry, choose ordered row/column packing versus a two-dimensional arrangement model, including perpendicular insertion, resize, and hidden-member rules. Also resolve whether fixed targets accept joins, whole-group merging, and the scope of restore-default actions. These remain explicit design decisions rather than assumed requirements.

## Snapping and membership commits

Separate geometric candidate calculation from grouping changes. Geometry returns adjusted bounds, guides, and candidate identity; a grouping resolver determines create/join versus alignment-only behavior.

- Start with a 6-physical-pixel acquisition threshold, a wider release threshold, and deterministic tie-breaking independent of GUI scale.
- Derive unsnapped bounds from starting geometry and total pointer displacement; never accumulate snapped corrections.
- Consider nearby visible item/group measured bounds and screen edges. Exclude the moving container and descendants, hidden content, handles, shadows, and tooltips; reject distant perpendicular spans.
- Use normal packing gaps for adjacency. Preview the destination arrangement without saving membership or recreating feature artwork.
- Reconcile clamping and native rounding before release. Remove a grouping candidate if its claimed adjacency no longer holds.
- Commit membership and placement together only on successful release. All cancellation paths discard the entire preview, including changes to the source group.

Grouped members subsequently move together; alignment-only snaps do not create relationships. Avoid nested groups and arbitrary inter-group attachment graphs.

## Persistence

Continue using native `GetDialogPosition`/`SetDialogPosition` for integer GUI-unit origins. X/Y pairs cannot encode arbitrary membership, order, layout policy, or custom group identities. Position presence alone no longer records detachment.

Add versioned membership/layout metadata keyed by stable indicator/container IDs. Prefer a supported native structured client setting if inspection confirms a suitable facility; otherwise use a VE-owned JSON layout file. Resolve that storage choice before implementation, without disguising metadata as coordinate keys.

Define authority and recoverable commit ordering across metadata and native origins before writing either. Two independent stores are not atomic: interrupted or failed drops must reconcile without orphaned members or half-applied layouts. Never persist previews or write on pointer movement.

Retain client-wide layouts across worlds. Define migration from existing native group positions into the initial arrangement and precedence for subsequent explicit membership. Reset only known VE keys/metadata; preserve unrelated dialogs and unavailable integrations. Validate versions, duplicate membership, invalid references, unknown IDs, and offscreen geometry. Keep saved origins through configuration changes, applying effective clamping without continuous preference writes.

## Integration boundaries

- **Membership and position integration:** resolve custom containers over defaults, persist committed topology and native coordinates through a defined recovery boundary.
- **Existing layout/session runtime:** apply effective placements, independent containers, packing, clipping, and availability consistently.
- **Native host and drag handle:** own hover targeting, pointer interaction, drag preview, cancellation, and commit requests.
- **Snap calculation and grouping resolver:** separate geometric candidates/guides from membership previews and commits; own no feature textures or readings.
- **Feature overlays/presentations:** continue supplying readings, measured sizes, visibility transitions, and drawing only.

The existing hover-label targeting already checks free-pointer state, drawable membership, and clipped bounds, and can supply the basis for handle targeting. Retain the host's selective input routing; simply calling `GuiDialog`'s default mouse methods would consume clicks over whole composer rectangles.

## Validation expectations

Start with a single draggable group prototype to establish whether the native titlebar can support hover-only rendering, selective input, cancellation, and persisted positions through the adapter. Verify that integration before extending it to individual containers and snapping. This prototype must not require changes to feature overlay or presentation classes.

Check coordinate conversion at multiple GUI scales; native saved-position round trips across client restarts; distinct group/indicator keys; reset behavior; resolution and style changes; group creation/insertion/removal and persistence recovery; consistent effective-anchor availability; snap tie-breaking and jitter; cancellation; and lifecycle cleanup. Position-only dragging must not refresh readings or recreate feature artwork.

In-game acceptance must cover free-cursor access, titlebar reachability at every screen edge, overlapping windows and focus, drag continuation outside bounds, no gameplay click-through during a drag, and ordinary input remaining unaffected away from handles. Source inspection establishes feasibility, not the final interaction quality.
