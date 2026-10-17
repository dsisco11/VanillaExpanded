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

### Hover control composition

Use the existing hover targeting to find a drawable indicator under a free cursor. Its titlebar and the containing group's grip are distinct targets, with an explicit target identity for each interaction.

Place the controls in a separate native control composition so their visibility never changes the measured content footprint or group packing. The control composition follows the target's effective bounds. Keep it visible while the pointer moves from the indicator onto the controls and throughout an active drag; hide it when neither region is hovered and no interaction remains active.

Prototype a narrow adapter around `GuiElementDialogTitleBar` that forwards native mouse events, gates rendering and hit testing to the hover/drag state, uses a stable dialog name for persistence, and observes the resulting parent bounds for layout updates and snapping. The control composition's origin and the persisted content origin must use a consistent mapping, including when the titlebar appears below an indicator near a screen edge.

The adapter must preserve the native Fixed/Movable behavior while ensuring cancellation does not commit a position. If the widget's private state or combined graphics/menu behavior makes this require replacing most of its implementation, use a small custom `GuiElement` with the same native position API. This is a prototype decision, not an assumption that the stock widget can be reused unchanged.

## Placement and group behavior

Keep registrations and built-in groups as the defaults. Resolve a user placement over those defaults before layout, rather than rewriting feature registrations or adding drag offsets inside presentation drawing.

The existing `HudOverlayGroupLayout` owns positions: it packs child offsets and reapplies the group's attachment whenever geometry changes. Mutating a child's bounds alone would therefore be temporary. An independently positioned indicator needs a separate effective layout container, while retaining its original registration identity and lifecycle ownership. That container can use the same native bounds and layout machinery as an ordinary group.

The effective placement must also inform scheduler anchor availability. Currently, the scheduler checks the registered group's anchor. A detached indicator with a screen placement must not disappear merely because its former hotbar anchor becomes unavailable. Resolve placement consistently for both availability and geometry; do not duplicate feature instances or sampling work.

For free placement, use the native titlebar convention: absolute top-left coordinates in unscaled GUI units. Resolve saved positions into screen placements through the existing `HudOverlayPlacement` vocabulary. Retain native GUI scaling and clamp effective bounds to the visible screen. Native position storage contains only an X/Y pair, so saved edge attachments or pivots are not part of this proposal. Size/style changes preserve the saved origin and repack grouped members normally.

Built-in placement configuration remains the fallback. A saved user placement takes precedence until reset; changing an unrelated feature setting must not erase it. Individually detached members stay detached when their original group is moved or reset; reset-all restores both placements and membership.

### Effective layout resolution

Resolve effective containers before invoking the existing packer:

| Position state | Effective layout |
| --- | --- |
| No individual override | The indicator remains in its registered group. |
| Saved individual override | The indicator uses an independent layout container. |
| Saved group override | The group uses the saved position and retains its internal packing. |

An active drag preview takes precedence over saved or default placement. Routine layout updates must consume that preview rather than overwrite the position with the registered attachment. For an individual drag, temporarily remove the indicator from its group's packed membership and preview its independent container. Commit saves its individual position; cancellation restores its previous placement and membership. The original group repacks accordingly.

Independent containers reuse native bounds, clipping, measurement, and rendering dispatch without duplicating feature instances or changing registration ownership. Clearing an individual position returns it to its registered group. Both the layout runtime and scheduler anchor checks consume the same resolved placement.

## Snapping

Start with geometric snapping, without introducing persistent docking relationships:

- Snap to screen edges and to the visible bounds of other VE indicators/groups. Exclude the moving target and, for a group drag, its descendants.
- Offer matching left/right/top/bottom edges, center lines, and adjacent edges separated by the existing group gap. Only consider nearby targets with overlapping or nearby perpendicular spans, so distant elements do not attract the drag.
- Use a small threshold, initially 6 physical pixels, independent of GUI scale. Keep a slightly wider release threshold to prevent jitter between close candidates. Break ties consistently and show alignment guides before release.
- Allow a modifier, proposed as Shift, to temporarily bypass snapping. Apply screen clamping before accepting the final preview so the committed position matches it.
- Snap against each indicator's measured footprint, not opaque texture pixels. Do not include transient handles, shadows, or tooltip bounds.
- Commit the resulting placement on release. Snapping does not silently regroup indicators or make them follow one another later.

### Drag calculation

1. Calculate an unsnapped candidate rectangle from the drag's starting content bounds and total pointer delta.
2. Collect screen edges and nearby visible indicator/group bounds, excluding the moving target and its descendants.
3. Calculate candidate adjustments for matching edges, center lines, and adjacent positions using the normal group gap.
4. Select the closest valid adjustment within the snap threshold, retaining the selected alignment until the wider release threshold is exceeded.
5. Produce the adjusted preview and alignment guides. Screen clamping must not leave a guide claiming an alignment the final rectangle no longer satisfies.
6. On release, commit the preview through native position persistence, matching its coordinate rounding.

Keep the unsnapped candidate separate from the snapped preview. Never accumulate snapped corrections into the next pointer delta; doing so would create sticky movement or cumulative position errors. Shift bypasses snapping using the same unsnapped candidate. The snap calculation accepts rectangles and returns geometry and guides, without owning feature state or GUI resources.

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

Start with a single draggable group prototype to establish whether the native titlebar can support hover-only rendering, selective input, cancellation, and persisted positions through the adapter. Verify that integration before extending it to individual containers and snapping. This prototype must not require changes to feature overlay or presentation classes.

Check coordinate conversion at multiple GUI scales; native saved-position round trips across client restarts; distinct group/indicator keys; reset behavior; resolution and style changes; group packing after detachment/reattachment; consistent effective-anchor availability; snap tie-breaking and jitter; cancellation; and lifecycle cleanup. Position-only dragging must not refresh readings or recreate feature artwork.

In-game acceptance must cover free-cursor access, titlebar reachability at every screen edge, overlapping windows and focus, drag continuation outside bounds, no gameplay click-through during a drag, and ordinary input remaining unaffected away from handles. Source inspection establishes feasibility, not the final interaction quality.
