# Quick-Tool Radial Menu Proposal

Status: Revised for action-time ItemStack reference lookup, native inventory swaps, and available-entry wedge construction (2026-09-23). Runtime acceptance remains in the plan.

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

It has no knowledge of tool ranking, inventory locations, item movement, or equipment restoration. It reports a selected entry identifier to the caller. Quick-tool outer entries retain the resolved item's game-localized name as entry data while the wedges show icons alone. Unavailable candidates have no wedge. Its public composition entry point remains thin; menu interaction and lifetime belong to the menu's owning module.

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

The center has a centered Unequip label and no bottom help text. Disable it when no supported unequip operation is available. A flip rejected before any movement leaves the inventory unchanged and provides brief feedback. A later failure in a multi-flip sequence must report interruption and reconcile the actual contents; it must never claim the original inventory remained unchanged.

Build one outer wedge per available cached entry. Order those entries by the canonical category table, with Light after tool categories, starting at screen up and proceeding clockwise. Available entries divide the ring equally. Acquisition or removal adds or removes a wedge and can move other entries; replacing a category's best item without changing availability updates its existing wedge. If no outer candidate is available, show only the center Unequip action, disabled when restoration is unavailable. A cache refresh that changes availability also updates the open menu's geometry and hit targets without closing it.

## Rendering

Use cached wedge geometry with shader-driven appearance. Generate and upload one combined mesh containing all category wedges and the center circle, with an entry identifier per vertex that remains constant across each triangle. Draw the menu geometry in one call over a fullscreen dimming layer. Use a warm dark orange and brown wedge palette that remains distinct from that layer. Render icons and the centered middle label with existing game rendering APIs, clipping each item icon to its own wedge.

Size the wheel to 60% of its previous screen radius, leave a visible angular gap between wedges, and keep the wedges slightly transparent. Scale the hovered outer wedge and its content to 115% for a bump effect while leaving its selection sector stable.

Generate geometry when first needed and reuse it across frames and menu openings while the geometric layout is unchanged. Rebuild when the available wedge count changes, when relative radii/layout change, or when resource recreation is necessary. A different available set with the same count can reuse geometry while replacing entry identifiers and text. Position and uniform scale use transforms. Hover, selection, enabled state, animation, and candidate/icon changes alone do not rebuild or re-upload geometry.

The shader uses supplied entry identifiers and state parameters for colors, highlighting, disabled appearance, animation, and edge effects. Wedge membership comes from the mesh rather than per-fragment angular classification over a full quad. Tessellate arcs to a defined screen-space visual tolerance across supported GUI scales and provide edge data for smooth antialiasing. Retessellation is permitted if scale exceeds cached tolerance or availability changes wedge count.

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

Place Light after available tool-category entries when it has a supported candidate. Omit its wedge when no candidate exists. Cache and refresh its candidate with the other entries, including changes to held/offhand items, active-hotbar selection, and light-selection inputs. Changing only the displayed light item keeps the current geometry; adding or removing Light changes the wedge count. Future virtual entries can supply their own selection policy through the same small entry/provider contract; no public plugin framework or additional special entries are required now.

Selecting a virtual entry uses the same client-owned equipment, restoration, and chained-selection rules as selecting a tool. Offhand is an eligible source for the light-source entry specifically, preserving the existing resolver's preference; ordinary tool-category eligibility is unchanged. Recheck the resolver and each native swap's source and destination restrictions before sending it. If the resolved candidate cannot participate in a supported sequence, disable or reject with feedback before the first swap. The active right-hand stack is excluded from every candidate provider, so it never receives a wedge. Switching tool to light or light to tool still restores the original item on Unequip. Different entries may resolve to the same other stack; keep both available entries and apply the same identity checks.

The [virtual-entry implementation contract](docs/quick-tool/VirtualEntryContract.md) records the shared selector boundary and offhand movement constraints, with installed-API evidence and explicit implementation verification limits.

## Equipment and Restoration

### Temporary Equipment Session

A successful local quick-tool selection starts a client-owned temporary equipment session when none exists. Retain the original `ItemStack` reference (or initially-empty-hand state), the current quick-tool's `ItemStack` reference, the original active hotbar position, and the quick-tool's preferred home address. Whole-stack quantities and the selected entry remain validation metadata. The home address identifies a return destination; it does not identify an item. Do not retain an expected displaced-item slot.

Before a switch or restoration, search the current eligible player inventory for the tracked `ItemStack` objects using reference identity. Resolve source slots from those searches, then validate the active hand, quantities, item usability, and destinations. Each required reference must occur exactly once. A lone missing reference or an ambiguous match ends the session without movement. If normal synchronization recreates both tracked objects, rebind only when each complete saved stack and quantity has one eligible match and the current tool remains in the recorded active hand. An initially empty hand needs only the current tool to match. No persistent item ID is introduced. Records do not reserve inventory slots.

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

When switching tools, return the current quick-tool to its original location and move the displaced original item to the newly selected tool's source location. The client performs the required native flips in sequence, using `TryFlipItems` and sending each returned inventory packet as the existing light shortcut does. The ordinary A/B/C example takes two flips during the B-to-C selection; selections themselves may be minutes apart. Precheck the intended arrangement and each next flip against current slots. If a local flip or validation fails after an earlier flip succeeds, retain the resulting inventory, stop the sequence, invalidate uncertain restoration history, and report interruption. Server validation of each packet happens separately; subsequent updates are handled by the game and the next action's reference lookup. Never describe a multi-flip sequence as atomic or roll back over player or server changes.

### Unequip

For an intact session, return the active quick-tool to its recorded original slot and restore the displaced item to the recorded hand slot. If the hand was initially empty, return the tool and leave that hand slot empty.

If the original tool slot is no longer usable, use the first compatible empty eligible slot in deterministic hotbar-then-backpack order. Preserve unrelated items. If no valid route is available before the first flip, leave the inventory unchanged and explain why the action could not complete. If an intervening change interrupts a started sequence, preserve the actual contents and report that restoration is unavailable. Never drop or delete an item to make restoration succeed.

Find the tracked stacks at their current locations before starting and validate the resulting slots before each native flip. The current quick-tool must still occupy the recorded active hand; otherwise end the session to preserve manual equipment intent. After the local sequence, verify the actual contents and update the session references and return destination. A local failure or interruption must report its actual outcome; later inventory notifications do not establish an accepted/rejected state for the session.

## Inventory Changes During a Session

Inventory notifications invalidate and refresh the candidate cache. Equipment sessions do not subscribe to slot changes to track movement or infer server rejection. Resolve and validate tracked stack references when opening the menu to determine restoration availability, and again before an equipment action. Do not scan inventory every frame.

Handling for external changes:

- Unrelated inventory changes refresh candidates and preserve the session.
- A manual change of active hotbar slot immediately ends the session. Detect replacement of the held item during the next menu-open or action validation, preventing restoration from overriding the player's new intent.
- Moving the original stack elsewhere in eligible inventory preserves restoration: the next lookup finds the same object at its new location. Moving the current quick-tool out of the recorded active hand ends the session when validated.
- Consumption, removal, breakage, splitting, merging, or object replacement is checked during the next validation. Changed whole-stack quantity, unusable item, lone reference replacement, or ambiguous matching ends restoration without substitution.
- World exit, player replacement, or feature disablement clears transient restoration state.

Invalidating a session does not move items automatically. An unequip action without a valid session is disabled. General unequipping of manually equipped tools is outside the initial scope.

## Authority and Failure Handling

Use the game's native inventory flip packet for each client-initiated swap. The client owns quick-tool intent and transient restoration history; the server's ordinary inventory handler validates and applies each flip. The client must not send arbitrary item contents or assume its predicted local flip was accepted when native synchronization disagrees. No quick-tool-specific server request, session, or result packet is required.

Guard against reentrant equipment calls only while the synchronous local flip sequence runs. When it finishes, the next action may run immediately after fresh reference lookup and validation. Do not wait for server packets, slot callbacks, a pending-result window, or a timeout. Native accepted updates and rejected-request corrections are applied by the game; their effect is considered during the next lookup without deriving a per-request accepted/rejected state. Rebuild affected cached candidates after inventory updates. A locally rejected or interrupted flip retains or invalidates prior history according to actual resulting contents.

## Scope Boundaries

The initial feature covers tool-category and virtual light-source selection, the reusable radial interaction, inventory-driven candidate caching, and reversible temporary equipment. It does not require custom player favorites, nested radial menus, tools from external containers, persistent restoration across reconnects, or an undo history of arbitrary inventory actions.

## Resolved Implementation Decisions

The [implementation contract](docs/quick-tool/ImplementationContract.md#resolved-decisions) resolves D1-D7: highest-tier then worn-first ranking; eligible own hotbar/backpack content; restoration across chained selections; manual hand changes ending the session; compatible empty-slot fallback; center disabled without a session; and canonical ordering of supported categories. The contract also records modded-item compatibility, authority, and synchronization boundaries. Available candidates determine the wedge count, including during an open interaction.

## Acceptance Criteria

- Holding the configured key opens the menu; releasing without a click changes no equipment.
- A selection click commits once, closes the menu, and does not also interact with the world.
- The center circle consistently represents unequip/restore.
- Each eligible category exposes its highest-ranked candidate under the chosen policy. Among usable tools of the same tier, the tool with the lowest remaining absolute durability wins; equal durability uses the deterministic inventory/slot tie-breaker.
- Inventory and ranking-relevant changes refresh cached results without a per-frame inventory scan.
- The ring contains exactly one wedge per available entry, in canonical order, plus the separate center action. Inventory reorderings and candidate upgrades that preserve the available set keep positions. Acquisition/removal redistributes wedges deterministically, including while the menu is open.
- Selecting tool B while holding item A, then selecting unequip, returns B to its original location and restores A when the arrangement remains valid.
- Selecting B then C then unequip restores A and returns both tools to their original slots when possible.
- Moving the original `ItemStack` to another eligible slot does not prevent restoration; lookup finds its current location. Normal paired object recreation can rebind unique full-stack matches; lone replacement and ambiguity end restoration safely. Immediate successive actions require no server response or slot callback, and inventory events continue refreshing the candidate cache independently.
- Initially empty hands, already-held candidates, duplicate tools, restricted slots, moved items, full inventories, broken tools, and rejected operations have deterministic behavior without item loss or duplication.
- The radial-menu system can present non-tool entries without depending on quick-tool inventory logic.
- Light source occupies a wedge after available tools when its shared selector finds a supported candidate and disappears when unavailable.
- Tool/light chains preserve original-item restoration; the same-stack, offhand-source restrictions, no-candidate, and stale-candidate cases cannot duplicate or lose items. Existing equip-light hotkey behavior is preserved.
- Cached combined wedge/center geometry is reused while wedge count and scale tolerance remain valid. Adding/removing available entries rebuilds geometry and hit targets; candidate-item changes at the same count update appearance without mesh uploads.
- Circular edges remain smooth at supported GUI scales, CPU hit testing agrees with visible targets, and shader/resource reload and disposal preserve correct rendering.
- Input ownership, subscriptions, cached references, and transient restoration state are cleaned up at the appropriate lifecycle boundaries.

## Implementation Evidence

The implementation contract and linked installed-API evidence record functionality registration, input/GUI ownership, inventory/durability observation, tool classification, authority, and item identity. The earlier server-assignment fixture is historical evidence for installed slot behavior, not proof that sequential native flips are atomic. Production eligibility, live GUI behavior, native persistence execution, and multiplayer reconciliation still require the implementation checklist verification.
