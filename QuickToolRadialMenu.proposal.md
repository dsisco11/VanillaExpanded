# Quick-Tool Radial Menu Proposal

Status: Approved

## Purpose

Provide a quick way to equip the best available tool from each tool category, then restore the item the player was previously holding. The player holds the assigned keybind to open a radial menu, moves the mouse toward an option, and clicks to select it. The center circle provides an unequip/restore action.

This document specifies approved intended behavior. The [implementation contract](docs/quick-tool/ImplementationContract.md) records the resolved policies, integration investigation, and evidence boundaries; it does not imply that the feature is implemented.

## Functional Requirements

- Implement the radial menu and quick-tool feature as separate functionality systems.
- Present one best available tool per supported tool category from the player's eligible inventory.
- Support virtual item-selection entries, initially Light source, reusing the existing equip-light-source selector.
- Precompute and cache available options, updating them when the player's inventory changes.
- Equip a selected tool into the player's active hand slot.
- Place the unequip option in the center circle.
- Return an equipped tool to its original inventory position when possible.
- Restore the item displaced by quick-tool selection when it remains available and restoration is valid.
- Use the game's supported inventory operations and synchronization model for all item movement.

## System Responsibilities

### Radial-Menu Functionality

The radial-menu system owns rendering, pointer interaction, highlighting, opening and closing, and selection reporting. It accepts a description of menu entries, including stable identifiers, labels, icons, enabled states, and a center entry.

It has no knowledge of tool ranking, inventory locations, item movement, or equipment restoration. It reports a selected entry identifier to the caller. Its public composition entry point remains thin; menu interaction and lifetime belong to the menu's owning module.

### Quick-Tool Functionality

The quick-tool system owns its keybind, inventory subscriptions, category selection, candidate ranking, cache maintenance, equipment operations, and restoration state. It supplies menu entries to the radial-menu system and handles selection results.

Keep candidate discovery, equipment restoration, and integration in files with distinct responsibilities within the quick-tool domain. These responsibilities do not require additional independent functionality systems.

Dependencies run from the quick-tool integration toward the reusable radial-menu interface. The menu must not depend on the quick-tool system.

## Interaction

1. Press and hold the configured keybind to open the menu.
2. Capture pointer input for menu navigation and suspend conflicting camera or gameplay input while it is open.
3. Highlight the entry toward which the pointer moves.
4. Click to select an enabled entry and close the menu.
5. Release the key without clicking to cancel without changing equipment.

After a selection, keep the menu closed until the player releases and presses the key again. Consume the selection click so it cannot also trigger a world interaction. Restore normal input ownership on every close path, including cancellation, loss of focus, feature disablement, and world exit.

The center is labeled Unequip, with explanatory text indicating restoration of the previous item when applicable. Disable it when no supported unequip operation is available. A rejected selection leaves the inventory unchanged and provides brief feedback. An operation committed before a later notification or synchronization failure is reported as committed and requiring reconciliation, never as an unchanged rejection.

Assign each supported tool category a fixed wedge in a canonical layout with a defined starting angle and direction. Preserve those positions across menu openings and inventory changes, independently of inventory enumeration order, tool acquisition order, candidate ranking, or current tool availability. Keep unavailable categories visible as disabled wedges. Replacing the best candidate updates the same wedge; inventory changes never rotate, compact, or resize the layout.

## Rendering

Use cached wedge geometry with shader-driven appearance. Generate and upload one combined mesh containing all category wedges and the center circle, with an entry identifier per vertex that remains constant across each triangle. Draw the background in one call; render icons and text separately using existing game rendering APIs.

Generate geometry when first needed and reuse it across frames and menu openings. Rebuild only for geometric layout changes, such as supported wedge count, angular spacing, or relative radii, or when resource recreation is necessary. Position and uniform scale use transforms. Hover, selection, enabled state, animation, and candidate/icon changes do not rebuild or re-upload geometry. Unavailable categories retain their fixed disabled wedges.

The shader uses supplied entry identifiers and state parameters for colors, highlighting, disabled appearance, animation, and edge effects. Wedge membership comes from the mesh rather than per-fragment angular classification over a full quad. Tessellate arcs to a defined screen-space visual tolerance across supported GUI scales and provide edge data for smooth antialiasing. Retessellation is permitted if a scale change exceeds the cached tessellation's supported tolerance, without changing category positions.

Perform pointer hit testing on the CPU using the same center, radii, angular layout, direction, and coordinate conversion as rendering. Define separator and boundary behavior consistently so selection matches the visible target. Supply the resolved hovered identifier to the shader; GPU readback is unnecessary.

The radial-menu system owns its renderer, geometry, shader, and resource lifecycle. Follow existing radial-progress rendering conventions where applicable while retaining separate menu-specific responsibilities. Handle shader reload, mesh disposal/recreation, and render-state restoration around icon and text drawing.

This rendering approach is approved without performance testing or comparative benchmarks. Correctness, visual quality, input alignment, and resource-lifecycle verification remain required; no measured performance advantage is claimed.

## Candidate Discovery and Cache

Scan eligible player-owned inventory locations and classify usable tools using the game's authoritative tool-category metadata where available. Container inventories merely opened by the player are excluded. Eligible types and slot exclusions are defined in the implementation contract.

Cache one candidate per category, including the information needed to render the entry and resolve its current inventory location. Treat cached locations as hints that require validation before an equipment operation.

Ranking within a category:

1. Highest tool tier.
2. Lowest remaining absolute durability among usable tools of the same tier, so tools closest to breaking are used up first.
3. A deterministic inventory and slot ordering to resolve ties.

Highest tier remains primary; lower remaining durability is preferred within the same tier. The implementation contract defines non-wearing, broken, unsupported, and incomparable tool metadata handling without inventing equivalence between unrelated categories.

Build the cache when the feature becomes available. Inventory changes invalidate relevant candidate data. Coalesce notifications produced by one inventory operation into a single refresh after the operation completes. A full scan of eligible inventory is an acceptable initial implementation if its cost is small; incremental indexing is not required by this proposal.

Durability and other ranking-relevant changes must also invalidate the cache. Verify whether the available inventory events cover these changes. Unsubscribe and discard references on world exit or feature disablement.

Opening the menu uses the cached options, refreshing pending invalidation first. Selecting an option revalidates the candidate and destination against current inventory state.

## Virtual Entries

Support virtual entries alongside tool categories. A virtual entry represents a semantic item selection, such as Light source, without requiring an EnumTool category. It resolves to a real existing inventory stack; it neither creates items nor defines a separate equipment mechanism. Use distinct stable identifiers, for example `tool:Pickaxe` and `virtual:light-source`, rather than fabricated enum values. The reusable radial-menu system receives ordinary entries and remains unaware of how their candidates were selected.

The initial virtual entry is Light source, covering lanterns, torches, oil lamps, and other items recognized by the existing equip-light-source feature. Reuse the selection logic in `VanillaExpanded/src/ModSystems/EquipLightSource.cs`: `ResolveLightSourceSlot`, `TryFindBrightestLightSource`, and `IsLightSource`. If access requires extraction, move that logic into one shared, side-independent light-source selector used by both features; do not duplicate it or invoke the existing hotkey's swapping behavior.

Preserve the current selector policy: offhand light first, active-hand light next, then the brightest light in the hotbar, then the brightest in the backpack. Light recognition and brightness currently use `Collectible.LightHsv[2] > 0`; equal brightness retains the first slot in the inventory's stable slot order. This is the existing feature's priority policy, not a global brightest-item search. Tool-tier and worn-first durability ranking applies to tool-category entries, not to the light-source provider. Do not introduce new fuel, durability, or brightness rules for the existing light hotkeys as part of this addition.

Give Light source a fixed wedge after the tool-category entries. Keep it visible but disabled when there is no supported candidate. Cache and refresh its candidate with the other entries, including changes to held/offhand items, active-hotbar selection, and light-selection inputs. Switching the displayed light never changes its wedge or rebuilds the menu mesh. Future virtual entries can supply their own selection policy through the same small entry/provider contract; no public plugin framework or additional special entries are required now.

Selecting a virtual entry uses the same server revalidation, active-hand equipment, original-item restoration, and chained-selection rules as selecting a tool. Offhand is an eligible source for the light-source entry specifically, preserving the existing resolver's preference; ordinary tool-category eligibility is unchanged. Verify offhand slot restrictions and the complete displaced-item/return arrangement before mutation. If the resolved candidate cannot participate in a valid supported arrangement, disable or reject with feedback without moving items. Selecting a light already held is a no-op; switching tool to light or light to tool still restores the original item on Unequip. Different entries may resolve to the same stack; keep both fixed entries and apply the same already-held/identity checks.

The [virtual-entry implementation contract](docs/quick-tool/VirtualEntryContract.md) records the shared selector boundary and offhand movement constraints, with installed-API evidence and explicit implementation verification limits.

## Equipment and Restoration

### Temporary Equipment Session

A successful quick-tool selection starts a temporary equipment session when none exists. Record the original hand slot, the original held item if any, the selected tool, its original inventory location, and the expected current location of the displaced item.

Track item identity using capabilities supported by the game's inventory model; an item code alone is insufficient when several identical tools exist. Verify stack identity, movement tracking, and synchronization behavior before choosing a concrete representation. Records describe expected contents and do not reserve inventory slots.

A tool already held should be a no-op selection and must not create an artificial restoration record.

### Switching Between Quick-Tools

Preserve the item held before the first quick-tool selection across subsequent selections. Unequip restores that original item, rather than the most recently used tool, following the chained-selection example below.

Example:

| Action | In hand | Pickaxe's original slot | Axe's original slot |
| --- | --- | --- | --- |
| Initial state | Item A | Pickaxe | Axe |
| Select pickaxe | Pickaxe | Item A | Axe |
| Select axe | Axe | Pickaxe | Item A |
| Select unequip | Item A | Pickaxe | Axe |

When switching tools, return the current quick-tool to its original location and move the displaced original item to the newly selected tool's source location. Validate the complete movement plan first, including slot restrictions. Commit using supported inventory operations without exposing item loss, duplication, or an unrecoverable partial result. The implementation contract specifies a server-owned, prevalidated whole-stack assignment followed by native persistence and synchronization; repeated client flip packets do not provide this boundary.

### Unequip

For an intact session, return the active quick-tool to its recorded original slot and restore the displaced item to the recorded hand slot. If the hand was initially empty, return the tool and leave that hand slot empty.

If the original tool slot is no longer usable, use the first compatible empty eligible slot in deterministic hotbar-then-backpack order. Preserve unrelated items. If no complete valid restoration is possible, leave the inventory unchanged and explain why the action could not complete. Never drop or delete an item to make restoration succeed.

Validate all involved items and slots again before committing. Clear restoration state only after confirmed success or an explicit session invalidation. A failed operation must not falsely report successful equipment or restoration.

## Inventory Changes During a Session

Distinguish changes caused by the quick-tool operation from external inventory changes. Internal notifications update the cache without prematurely invalidating the session.

Handling for external changes:

- Unrelated inventory changes refresh candidates and preserve the session.
- A manual change of active hotbar slot or replacement of the held item ends the session, preventing a later restore from overriding the player's new intent.
- Movement of a recorded item preserves the session only if its identity and new location can be established reliably; otherwise invalidate restoration.
- Consumption, removal, breakage, or replacement of an involved item requires revalidation. Do not substitute an identical-looking item without a supported identity contract.
- World exit, player replacement, or feature disablement clears transient restoration state.

Invalidating a session does not move items automatically. An unequip action without a valid session is disabled. General unequipping of manually equipped tools is outside the initial scope.

## Authority and Failure Handling

Resolve the existing authoritative path for inventory movement before implementation. In multiplayer, client-side menu and cache state must not be treated as authority to mutate inventory. Revalidate on the authoritative side wherever required by the game's architecture.

Avoid overlapping equipment requests. While an operation awaits confirmation, prevent another selection from acting on unconfirmed restoration state. Update the session from the accepted result and rebuild affected cached candidates. Rejected operations retain or invalidate prior state according to the actual resulting inventory, never an assumed successful swap.

## Scope Boundaries

The initial feature covers tool-category and virtual light-source selection, the reusable radial interaction, inventory-driven candidate caching, and reversible temporary equipment. It does not require custom player favorites, nested radial menus, tools from external containers, persistent restoration across reconnects, or an undo history of arbitrary inventory actions.

## Resolved Implementation Decisions

The [implementation contract](docs/quick-tool/ImplementationContract.md#resolved-decisions) resolves D1-D7: highest-tier then worn-first ranking; eligible own hotbar/backpack content; restoration across chained selections; manual hand changes ending the session; compatible empty-slot fallback; center disabled without a session; and the explicit stable category mapping. The contract also records modded-item compatibility, authority, and synchronization boundaries. Its canonical category table is independent of the player's inventory; unavailable categories retain disabled wedges. Only supported-category-set or explicit layout changes may rebuild layout geometry, never while the menu is open.

## Acceptance Criteria

- Holding the configured key opens the menu; releasing without a click changes no equipment.
- A selection click commits once, closes the menu, and does not also interact with the world.
- The center circle consistently represents unequip/restore.
- Each eligible category exposes its highest-ranked candidate under the chosen policy. Among usable tools of the same tier, the tool with the lowest remaining absolute durability wins; equal durability uses the deterministic inventory/slot tie-breaker.
- Inventory and ranking-relevant changes refresh cached results without a per-frame inventory scan.
- Each category retains the same wedge across menu openings, inventory reorderings, tool acquisition or removal, and best-candidate changes. Unavailable categories retain disabled wedges; the remaining wedges never compact or redistribute. The same supported category set and layout configuration produce identical positions regardless of registration or inventory enumeration order.
- Selecting tool B while holding item A, then selecting unequip, returns B to its original location and restores A when the arrangement remains valid.
- Selecting B then C then unequip restores A and returns both tools to their original slots when possible.
- Initially empty hands, already-held candidates, duplicate tools, restricted slots, moved items, full inventories, broken tools, and rejected operations have deterministic behavior without item loss or duplication.
- The radial-menu system can present non-tool entries without depending on quick-tool inventory logic.
- Light source occupies a fixed disabled-or-enabled wedge, resolves through shared existing light-selection logic, and refreshes when inventory or hand-priority inputs change.
- Tool/light chains preserve original-item restoration; the same-stack, offhand-source restrictions, no-candidate, and stale-candidate cases cannot duplicate or lose items. Existing equip-light hotkey behavior is preserved.
- Cached combined wedge/center geometry is reused across frames and reopenings; state and candidate changes update appearance without mesh rebuilds or uploads.
- Circular edges remain smooth at supported GUI scales, CPU hit testing agrees with visible targets, and shader/resource reload and disposal preserve correct rendering.
- Input ownership, subscriptions, cached references, and transient restoration state are cleaned up at the appropriate lifecycle boundaries.

## Implementation Evidence

The implementation contract and linked installed-API evidence record functionality registration, input/GUI ownership, inventory/durability observation, tool classification, authority, and item identity. Its isolated movement fixture demonstrates the underlying commit boundary. Production eligibility, live GUI behavior, native persistence execution, and multiplayer reconciliation still require the implementation checklist verification; they are not implied by the investigation.
