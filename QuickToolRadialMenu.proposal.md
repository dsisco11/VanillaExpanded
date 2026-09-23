# Quick-Tool Radial Menu Proposal

Status: Approved

## Purpose

Provide a quick way to equip the best available tool from each tool category, then restore the item the player was previously holding. The player holds the assigned keybind to open a radial menu, moves the mouse toward an option, and clicks to select it. The center circle provides an unequip/restore action.

This document specifies intended behavior. Repository integration points and inventory API capabilities have not yet been verified.

## Functional Requirements

- Implement the radial menu and quick-tool feature as separate functionality systems.
- Present one best available tool per supported tool category from the player's eligible inventory.
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

The center is labeled Unequip, with explanatory text indicating restoration of the previous item when applicable. Disable it when no supported unequip operation is available. A failed selection leaves the inventory unchanged and provides brief feedback.

Assign each supported tool category a fixed wedge in a canonical layout with a defined starting angle and direction. Preserve those positions across menu openings and inventory changes, independently of inventory enumeration order, tool acquisition order, candidate ranking, or current tool availability. Keep unavailable categories visible as disabled wedges. Replacing the best candidate updates the same wedge; inventory changes never rotate, compact, or resize the layout.

## Candidate Discovery and Cache

Scan eligible player-owned inventory locations and classify usable tools using the game's authoritative tool-category metadata where available. Container inventories merely opened by the player are excluded. The precise eligible inventory types must be established during integration.

Cache one candidate per category, including the information needed to render the entry and resolve its current inventory location. Treat cached locations as hints that require validation before an equipment operation.

Proposed ranking within a category:

1. Highest tool tier.
2. Lowest remaining absolute durability among usable tools of the same tier, so tools closest to breaking are used up first.
3. A deterministic inventory and slot ordering to resolve ties.

The preference for lower remaining durability is confirmed. Tier precedence remains the proposed primary ranking criterion and requires confirmation before implementation. Unsupported or incomparable modded tool metadata needs a documented fallback; avoid inventing equivalence between unrelated categories.

Build the cache when the feature becomes available. Inventory changes invalidate relevant candidate data. Coalesce notifications produced by one inventory operation into a single refresh after the operation completes. A full scan of eligible inventory is an acceptable initial implementation if its cost is small; incremental indexing is not required by this proposal.

Durability and other ranking-relevant changes must also invalidate the cache. Verify whether the available inventory events cover these changes. Unsubscribe and discard references on world exit or feature disablement.

Opening the menu uses the cached options, refreshing pending invalidation first. Selecting an option revalidates the candidate and destination against current inventory state.

## Equipment and Restoration

### Temporary Equipment Session

A successful quick-tool selection starts a temporary equipment session when none exists. Record the original hand slot, the original held item if any, the selected tool, its original inventory location, and the expected current location of the displaced item.

Track item identity using capabilities supported by the game's inventory model; an item code alone is insufficient when several identical tools exist. Verify stack identity, movement tracking, and synchronization behavior before choosing a concrete representation. Records describe expected contents and do not reserve inventory slots.

A tool already held should be a no-op selection and must not create an artificial restoration record.

### Switching Between Quick-Tools

The proposed behavior preserves the item held before the first quick-tool selection across subsequent selections. Unequip restores that original item, rather than the most recently used tool. This extends the requested single-swap behavior and remains a design decision to confirm.

Example:

| Action | In hand | Pickaxe's original slot | Axe's original slot |
| --- | --- | --- | --- |
| Initial state | Item A | Pickaxe | Axe |
| Select pickaxe | Pickaxe | Item A | Axe |
| Select axe | Axe | Pickaxe | Item A |
| Select unequip | Item A | Pickaxe | Axe |

When switching tools, return the current quick-tool to its original location and move the displaced original item to the newly selected tool's source location. Validate the complete movement plan first, including slot restrictions. Commit using supported inventory operations without exposing item loss, duplication, or an unrecoverable partial result. Whether the inventory API can support this transaction directly must be verified.

### Unequip

For an intact session, return the active quick-tool to its recorded original slot and restore the displaced item to the recorded hand slot. If the hand was initially empty, return the tool and leave that hand slot empty.

If the original tool slot is no longer usable, the proposed fallback is another eligible slot that can accept the tool. Preserve unrelated items. If no complete valid restoration is possible, leave the inventory unchanged and explain why the action could not complete. Never drop or delete an item to make restoration succeed.

Validate all involved items and slots again before committing. Clear restoration state only after confirmed success or an explicit session invalidation. A failed operation must not falsely report successful equipment or restoration.

## Inventory Changes During a Session

Distinguish changes caused by the quick-tool operation from external inventory changes. Internal notifications update the cache without prematurely invalidating the session.

Proposed handling for external changes:

- Unrelated inventory changes refresh candidates and preserve the session.
- A manual change of active hotbar slot or replacement of the held item ends the session, preventing a later restore from overriding the player's new intent.
- Movement of a recorded item preserves the session only if its identity and new location can be established reliably; otherwise invalidate restoration.
- Consumption, removal, breakage, or replacement of an involved item requires revalidation. Do not substitute an identical-looking item without a supported identity contract.
- World exit, player replacement, or feature disablement clears transient restoration state.

Invalidating a session does not move items automatically. An unequip action without a valid session is disabled under the initial proposal. General unequipping of manually equipped tools is an open scope decision.

## Authority and Failure Handling

Resolve the existing authoritative path for inventory movement before implementation. In multiplayer, client-side menu and cache state must not be treated as authority to mutate inventory. Revalidate on the authoritative side wherever required by the game's architecture.

Avoid overlapping equipment requests. While an operation awaits confirmation, prevent another selection from acting on unconfirmed restoration state. Update the session from the accepted result and rebuild affected cached candidates. Rejected operations retain or invalidate prior state according to the actual resulting inventory, never an assumed successful swap.

## Scope Boundaries

The initial feature covers tool-category selection, the reusable radial interaction, inventory-driven candidate caching, and reversible temporary equipment. It does not require custom player favorites, nested radial menus, tools from external containers, persistent restoration across reconnects, or an undo history of arbitrary inventory actions.

## Decisions to Confirm

- Whether highest tool tier remains the primary ranking criterion. Within the same tier, lower remaining absolute durability is preferred; that direction is confirmed.
- Which player inventories and tool categories are eligible, including treatment of modded tools.
- Whether chained selections restore the original held item as proposed.
- Whether manual hotbar selection ends the restoration session as proposed.
- Whether occupied-slot fallback should use any valid empty player inventory slot or fail unless the original arrangement can be restored.
- Whether the center action should support manually equipped tools when no restoration session exists.
- The canonical category-to-wedge mapping, starting angle, direction, and deterministic layout-extension policy for modded categories. Unavailable categories retain disabled wedges. The quick-tool system supplies category positions and the radial-menu system preserves them. Only changes to the supported category set or explicit layout configuration may rebuild the layout, never while the menu is open.

## Acceptance Criteria

- Holding the configured key opens the menu; releasing without a click changes no equipment.
- A selection click commits once, closes the menu, and does not also interact with the world.
- The center circle consistently represents unequip/restore.
- Each eligible category exposes its highest-ranked candidate under the chosen policy. Among usable tools of the same tier, the tool with the lowest remaining absolute durability wins; equal durability uses the deterministic inventory/slot tie-breaker.
- Inventory and ranking-relevant changes refresh cached results without a per-frame inventory scan.
- Each category retains the same wedge across menu openings, inventory reorderings, tool acquisition or removal, and best-candidate changes. Unavailable categories retain disabled wedges; the remaining wedges never compact or redistribute. The same supported category set and layout configuration produce identical positions regardless of registration or inventory enumeration order.
- Selecting tool B while holding item A, then selecting unequip, returns B to its original location and restores A when the arrangement remains valid.
- If chained restoration is accepted, selecting B then C then unequip restores A and returns both tools to their original slots when possible.
- Initially empty hands, already-held candidates, duplicate tools, restricted slots, moved items, full inventories, broken tools, and rejected operations have deterministic behavior without item loss or duplication.
- The radial-menu system can present non-tool entries without depending on quick-tool inventory logic.
- Input ownership, subscriptions, cached references, and transient restoration state are cleaned up at the appropriate lifecycle boundaries.

## Implementation Investigation

Before preparing an implementation plan, identify the repository's functionality registration and keybind conventions, reusable GUI/input facilities, inventory-change and durability notifications, tool classification and ranking metadata, authoritative swap operations, and item identity capabilities. Validate the multi-slot restoration contract against those APIs before committing to a concrete movement algorithm.
