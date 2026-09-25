# Virtual-entry implementation contract

Planning amendment investigation, 2026-09-23. Authority: [revised proposal](../../QuickToolRadialMenu.proposal.md#virtual-entries) and the two incremental phase 1 tasks in the [plan](../../QuickToolRadialMenu.todo). This extends [ImplementationContract.md](ImplementationContract.md). The shared-selector policy remains valid; the original server-owned movement conclusions below are superseded by the client native-swap revision.

## Shared selection boundary

Move the selection policy from `VanillaExpanded/src/ModSystems/EquipLightSource.cs` into `VanillaExpanded/src/Lighting/LightSourceSelection.cs`. The owner is independent of both functionality systems and requires only common inventory/collectible APIs and inventory-class constants. Preserve the resolver inputs `IPlayerInventoryManager playerInventory, ItemSlot offhandSlot` and the left/right-hand result flags needed by the existing shortcuts. Return the existing slot reference, without cloning, mutating, sending packets, or requiring `IPlayer` or a client API.

Move `ResolveLightSourceSlot`, `TryFindBrightestLightSource`, and both `IsLightSource` overloads together. Migrate both consumers and the selection tests to this single policy owner. The existing shortcut retains its equipment behavior; quick-tool uses the same native flip mechanism with separate restoration history. Shared selection must remain usable when the separate light-shortcut feature is disabled. Avoid copying the algorithm or introducing redundant forwarding methods merely to preserve test reflection helpers.

The unchanged policy is offhand light first, active-hand light second, brightest hotbar light third, brightest backpack light last. Recognition and brightness use positive `Collectible.LightHsv[2]`. Strictly greater brightness replaces the current winner; equal brightness retains the first enumerated slot. There is no global sorting across inventories, dynamic-light/fuel rule, tier rule, or worn-first rule for this provider. Existing tests in `VanillaExpanded.Tests/Unit/EquipLightSource/` document detection, hand priority, inventory priority, and equal-brightness behavior; these tests were inspected, not claimed as executed by this investigation.

The client obtains the current player's own inventory manager and physical offhand slot. Rerun resolution at selection rather than trusting a cached slot. Validate the selected slot's owner and reference against the player's current supported inventories. The light provider admits the physical offhand in addition to ordinary supported hotbar/backpack storage; tool-category discovery still excludes it. Preserve the resolver's full legacy inventory enumeration, then validate its returned source. A light returned from an unsupported special slot is unavailable; do not filter the resolver's inputs or quietly substitute another candidate.

## Offhand movement boundary

An offhand source is an ordinary reference-bearing slot with an Offhand storage restriction, not an extra inventory or a special item identity. Verify the physical slot through the owning hotbar's slot lookup and the player's Entity.LeftHandItemSlot reference rather than accepting an arbitrary supplied slot. Installed details and fixture coverage are recorded in [VirtualEntryEvidence.md](VirtualEntryEvidence.md).

Precheck the intended whole-stack arrangement and the next native flip against ownership, unchanged contents/quantities, take/put locks, storage flags, tags, container restrictions, and capacity. The game's ordinary server inventory handler separately validates each sent flip. An offhand destination needs an Offhand-compatible incoming item. Do not assume offhand capacity is one. An empty incoming value clears a slot and does not require `CanHold` on a nonexistent stack, but source removal and session validation still apply.

For active hand H, offhand O, ordinary tool sources S and T, original item A, tools B/C, and offhand light L, the supported mixed chain is:

| Operation | H | S | O | T |
| --- | --- | --- | --- | --- |
| Initial | A | B | L | C |
| Select B | B | A | L | C |
| Select L | L | B | A | C |
| Select C | C | B | L | A |
| Unequip | A | B | L | C |

Each row transition is an intended final arrangement reached through one or more client native flips. For example, switching B to L uses H-with-S followed by H-with-O; those flips are separate server operations. A must fit O for the latter flip. If A is incompatible before starting, reject unchanged even if another light exists. If a later flip fails after an earlier one succeeds, retain actual contents, invalidate uncertain history, and report interruption. The existing return-home fallback policy still governs a later unavailable return slot. Direct A-to-L-to-A is the corresponding one-flip selection and one-flip restoration; an initially empty hand is supported. A candidate already in H is a no-op and cannot replace the original session item. Selecting the tracked original item uses the existing restoration rule.

Retain ItemStack references and validation metadata exactly as for tools. Find each tracked stack in current eligible inventory at menu open and before equipment actions; offhand is included where the shared contract permits it. Retain the home address only as a return destination. Missing or replaced references end restoration without matching equal-looking stacks or adding item UUIDs. Inventory notifications refresh the light candidate cache without driving equipment-session reconciliation. Restoring through O revalidates its current destination restrictions.

## Notifications, persistence, and cache invalidation

The offhand belongs to the player's hotbar, so its modifications use the existing cache subscription and native inventory path. Other involved bag-content slots retain their ordinary native persistence callbacks. Production movement calls `TryFlipItems` and sends its returned packet for each flip. An interrupted local sequence preserves already-applied flips and clears uncertain history. Subsequent synchronization is handled by the game; the equipment owner validates stack references on the next action without waiting for or classifying inventory updates.

Hotbar SlotModified includes offhand changes; active-hotbar changes also invalidate the light cache because hand preference can change without a stack moving. Reconcile inventory replacement/topology and subscriptions as already specified. Metadata changes retain the existing dirty-notification compatibility contract. Client hints may be stale; rerun the shared resolver before the first native flip and recheck each following flip.

Installed IL and isolated fixtures establish the available seams and slot constraints. The earlier direct-assignment fixture does not verify the revised native flip sequence. Native multiplayer delivery, real-world persistence, interruption behavior, selector extraction, and regression execution remain implementation acceptance work. No performance testing is required for this investigation.

## Amendment traceability and second review

| Incremental task | Controlling sources | Investigation result |
| --- | --- | --- |
| Shared selector and client/server source eligibility | Proposal Virtual Entries; EquipLightSource selection methods; existing selection tests; common inventory-manager contract | A side-independent inventory-manager/slot seam already exists; move one policy owner and migrate both callers. Preserve all legacy priorities/ties; validate the returned source separately. |
| Offhand validation, identity, persistence, restoration | Proposal Equipment and Virtual Entries; original movement contract; ItemSlotOffhand/ItemSlot/InventoryBase; installed probe evidence | Same reference transaction and native owning-hotbar path; Offhand storage eligibility is an additional destination constraint; mixed chains preserve A and reject incompatible plans before mutation. |

Second review: Reconciled selector priority against equipment eligibility, distinguished an unsupported selected source from no light, and checked that shared selection does not depend on the shortcut feature's client-only lifecycle. Reviewed the mixed-chain assignments for restoration of both B and L, unchanged rejection, empty-hand handling, identity continuity, and inherited notifications. The installed fixture passed all six scenario groups and was independently rerun successfully. The [independent amendment audit](VirtualEntryCompletionAudit.md) passed; the two investigation markers are complete. Original audit files remain historical evidence for the original contract.
