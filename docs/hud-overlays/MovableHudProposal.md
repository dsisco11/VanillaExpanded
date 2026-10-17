# Movable HUD indicators

## Recommendation

Reuse Vintage Story's HUD/dialog lifecycle, GUI event routing, native bounds and scaling, and native dialog-position persistence. Prefer the stock titlebar's drag behavior where it can be adapted to hover controls; extend the existing VE layout runtime for individual placement and geometric snapping. Individual feature overlays and their presentation classes should not implement dragging or persistence.

This is a design proposal; the current HUD remains passive and automatically packed.

## Base-game findings

The investigation used the local API source and the installed Vintage Story 1.22.7 client assembly.

| Facility | Verified behavior | Reuse |
| --- | --- | --- |
| `HudElement` / `GuiDialog` | HUDs are native dialogs. `HudElement` does not request an ungrabbed mouse. Mouse delivery and input capture are separately overridable. | Keep the existing native HUD host; enable narrowly scoped pointer interaction. |
| `GuiComposer` / `GuiElement` | Composers dispatch mouse down, move, and up to interactive elements. Bounds support hit testing and native parent/child geometry. | Implement the handle as a native GUI element, using the existing compositions. |
| `GuiElementDialogTitleBar` | Its Fixed/Movable menu enables dragging. Mouse movement updates parent `fixedX/Y` using pixel delta divided by GUI scale. Release stores the position. | Prefer native dragging, with an adapter for hover visibility and snapping. |
| `IGuiAPI.GetDialogPosition` / `SetDialogPosition` | Installed `GuiAPI` delegates directly to global `ClientSettings`. | Use as the position authority, with stable VE namespaced keys. No separate layout file is required. |
| `GetDialogBoundsInArea` | Installed implementation returns open composer bounds whose `Alignment` equals the requested dialog area. | Bounds discovery only; this is not a snapping or docking service. |
| `GuiDialog.GetFreePos` / `OccupyPos` / `FreePos` | Reserves predefined flag-based window positions. | Not appropriate for arbitrary player placement or edge alignment. |

No general snapping or docking facility was found in the inspected native GUI API and window-drag path. Stock dragging changes positions freely; it does not calculate neighboring edges or snap relationships.

The stock titlebar already supplies the essential dragging and persistence. Its direct use of native client settings is suitable. It is not a complete drop-in solution for the proposed hover-only handles: its movable/moving state and setup methods are private, it includes close/menu controls, and it exposes no public drag-completed callback. Prefer a narrow adapter around native event handling and parent bounds, with snapping applied before release stores the position. Hover rendering, small indicator dimensions, and cancellation need validation before claiming that the widget can be reused unchanged. If those constraints require a smaller custom handle, it should still use native position storage; a new persistence subsystem is unnecessary.

Native `GuiManager` dispatches mouse events through registered dialogs until one handles them. A handled mouse-down also invokes its focus-management path, which can unfocus another dialog even if the HUD itself is non-focusable. Interaction must respect foreground windows and must be checked with inventories and other dialogs open; setting `Focusable = false` alone does not guarantee that other dialog focus remains untouched.

Source references: [titlebar](../../../vsapi/Client/UI/Elements/Impl/Interactive/GuiElementDialogTitleBar.cs), [HUD base](../../../vsapi/Client/UI/HudElement.cs), [dialog input](../../../vsapi/Client/UI/Dialog/GuiDialog.cs), [composer](../../../vsapi/Client/UI/GuiComposer.cs), [bounds](../../../vsapi/Client/UI/ElementBounds.cs), [GUI API](../../../vsapi/Client/API/IGuiAPI.cs). Installed `GuiManager` and `GuiAPI` implementations were also inspected to verify dispatch and storage behavior.

## Player interaction

- Show handles only while the mouse is ungrabbed, the HUD is visible, and no foreground dialog owns that pointer location. Normal gameplay keeps the existing passive behavior.
- Hovering an indicator reveals its title and a small drag grip. A separate group grip identifies the containing group, so moving an indicator and moving its group are distinct actions.
- Handles are transient overlays. They do not enlarge measured indicator footprints, shift neighboring indicators, or trigger content preparation. Place them above the target when space permits and below it at the screen edge. Keep the handle visible while the pointer travels from the indicator to it, and throughout dragging.
- Left-drag a grip to move its target. Consume pointer events only for a grip interaction or active drag; leave clicks elsewhere unchanged. Continue the drag outside the original bounds until release.
- A group drag moves its currently grouped members together and preserves their packing. Dragging an individual indicator away detaches it from automatic packing and gives it its own placement. Remaining members close the gap.
- A detached indicator no longer moves with its original group. Provide a handle menu action to return it to that group, a group action to restore default placement, and a reset-all action.
- Escape cancels an active drag and restores its starting placement. Mouse capture changes, world exit, GUI hiding, target removal, or loss of the active interaction cancel safely rather than saving a partial drag.
- Hidden/inapplicable indicators retain their saved placements but do not expose invisible drag targets. They can be moved when visible; an editor for hidden indicators would be a separate optional extension.

## Placement and group behavior

Keep registrations and built-in groups as the defaults. Resolve a user placement over those defaults before layout, rather than rewriting feature registrations or adding drag offsets inside presentation drawing.

The existing `HudOverlayGroupLayout` owns positions: it packs child offsets and reapplies the group's attachment whenever geometry changes. Mutating a child's bounds alone would therefore be temporary. An independently positioned indicator needs a separate effective layout container, while retaining its original registration identity and lifecycle ownership. That container can use the same native bounds and layout machinery as an ordinary group.

The effective placement must also inform scheduler anchor availability. Currently, the scheduler checks the registered group's anchor. A detached indicator with a screen placement must not disappear merely because its former hotbar anchor becomes unavailable. Resolve placement consistently for both availability and geometry; do not duplicate feature instances or sampling work.

For free placement, use the native titlebar convention: absolute top-left coordinates in unscaled GUI units. Resolve saved positions into screen placements through the existing `HudOverlayPlacement` vocabulary. Retain native GUI scaling and clamp effective bounds to the visible screen. Native position storage contains only an X/Y pair, so saved edge attachments or pivots are not part of this proposal. Size/style changes preserve the saved origin and repack grouped members normally.

Built-in placement configuration remains the fallback. A saved user placement takes precedence until reset; changing an unrelated feature setting must not erase it. Individually detached members stay detached when their original group is moved or reset; reset-all restores both placements and membership.

## Snapping

Start with geometric snapping, without introducing persistent docking relationships:

- Snap to screen edges and to the visible bounds of other VE indicators/groups. Exclude the moving target and, for a group drag, its descendants.
- Offer matching left/right/top/bottom edges, center lines, and adjacent edges separated by the existing group gap. Only consider nearby targets with overlapping or nearby perpendicular spans, so distant elements do not attract the drag.
- Use a small threshold, initially 6 physical pixels, independent of GUI scale. Keep a slightly wider release threshold to prevent jitter between close candidates. Break ties consistently and show alignment guides before release.
- Allow a modifier, proposed as Shift, to temporarily bypass snapping. Apply screen clamping before accepting the final preview so the committed position matches it.
- Snap against each indicator's measured footprint, not opaque texture pixels. Do not include transient handles, shadows, or tooltip bounds.
- Commit the resulting placement on release. Snapping does not silently regroup indicators or make them follow one another later.

This yields precise alignment with a small amount of layout logic. Persistent docking would additionally require attachment relationships, cycle prevention, hidden-target behavior, and propagation after size changes. It should be an explicit later decision if “snapped together” needs to mean “remain attached.” Existing packed groups already provide that behavior for their members.

## Persistence

Use `IGuiAPI.GetDialogPosition` and `SetDialogPosition`, backed by the game's client settings. Positions are local to the client and shared across worlds. No separate VE JSON file or custom serializer is needed.

Give each movable group and individual indicator a distinct, stable composer/dialog key, such as `vanillaexpanded:hud:group:world-status` and `vanillaexpanded:hud:indicator:vanillaexpanded:player-wetness`. Save native `Vec2i` GUI-unit positions. A missing group entry means its registered placement applies; a missing indicator entry means it participates in its registered group's packing. Presence of an individual position therefore also records detachment, without a separate membership file.

Let the native titlebar restore and store its position where used. Any custom handle uses the same API after a successful drop. Reattachment or reset clears the relevant key with `SetDialogPosition(key, null)`; reset-all clears keys for the known VE groups and indicators. Do not erase another dialog's settings. Leave positions for temporarily unavailable integrations intact. Avoid writes on each mouse move and leave serialization and disk persistence to the engine. Validate restored positions and keep effective bounds reachable after viewport changes; match native coordinate rounding rather than introducing another position format.

## Integration boundaries

- **Position integration:** read/reset native dialog-position entries and resolve them over registered defaults.
- **Existing layout/session runtime:** apply effective placements, independent containers, packing, clipping, and availability consistently.
- **Native host and drag handle:** own hover targeting, pointer interaction, drag preview, cancellation, and commit requests.
- **Snap calculation:** operate on supplied rectangles and return an adjusted candidate plus alignment guides; own no textures or feature state.
- **Feature overlays/presentations:** continue supplying readings, measured sizes, visibility transitions, and drawing only.

The existing hover-label targeting already checks free-pointer state, drawable membership, and clipped bounds, and can supply the basis for handle targeting. The current host's blanket input refusal must become selective routing; simply calling `GuiDialog`'s default mouse methods would consume clicks over whole composer rectangles.

## Validation expectations

Check coordinate conversion at multiple GUI scales; native saved-position round trips across client restarts; distinct group/indicator keys; reset behavior; resolution and style changes; group packing after detachment/reattachment; consistent effective-anchor availability; snap tie-breaking and jitter; cancellation; and lifecycle cleanup. Position-only dragging must not refresh readings or recreate feature artwork.

In-game acceptance must cover free-cursor access, titlebar reachability at every screen edge, overlapping windows and focus, drag continuation outside bounds, no gameplay click-through during a drag, and ordinary input remaining unaffected away from handles. Source inspection establishes feasibility, not the final interaction quality.
