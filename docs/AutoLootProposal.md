# AutoLoot Proposal

Status: Proposed

## Summary

Add AutoLoot as a server-authoritative bulk transfer from a lootable entity
corpse into the player's inventory. Holding Interact displays the same radial
progress indicator used by AutoStash; completing the hold requests collection
of all available loot.

Reduce duplication by sharing hold timing, progress presentation, and low-level
inventory operations. Keep corpse eligibility and loot transfer rules separate
from AutoStash's matching-item policies.

This document describes the recommended design, not an implemented feature.
The vanilla corpse interaction and inventory lifecycle must be verified against
the supported game version before choosing the final integration hooks.

## Goals

- Provide a familiar hold-to-act interaction consistent with AutoStash.
- Collect every available corpse item that fits in the player's inventory.
- Preserve vanilla harvesting, access restrictions, inventory synchronization,
  and corpse lifecycle behavior.
- Reuse existing radial rendering and inventory movement infrastructure.
- Preserve existing AutoStash gestures, timing, and transfer behavior.

## First-Release Scope

Support standard engine-backed corpse loot inventories.

Exclude automatic skinning, butchering, harvesting, loot generation, loot
filters, nearby-corpse collection, and general container "take all" behavior.
Do not assume that player corpses, modded corpses, attached bags, or arbitrary
entity inventories are eligible merely because they contain items.

## Recommended Player Experience

1. The player holds Interact while targeting an eligible corpse.
2. After a short grace period, a radial indicator appears with a localized
   "Auto Looting" label.
3. When the configured delay completes, the client requests the transfer.
4. The server transfers every item it can into the player's inventory.
5. Items that do not fit remain on the corpse.

A normal click should still open the vanilla loot interface. Holding Interact
must coexist with that interface without repeatedly opening or closing it.

Progress cancels when the player releases Interact, changes targets, moves out
of reach, loses eligibility, or disables the feature. Completion sends only one
request per continuous hold. Another attempt requires releasing and holding
again.

Living entities and empty or otherwise ineligible corpses do not trigger
progress. AutoLoot collects already-available loot; it does not bypass a
harvesting prerequisite or generate drops.

## Existing Infrastructure and Reuse

| Area | Existing implementation | Proposed approach |
| --- | --- | --- |
| Radial rendering | [ModSystemRadialProgressBar](../VanillaExpanded/src/RadialProgress/ModSystemRadialProgressBar.cs) and [IProgressSystemProvider](../VanillaExpanded/src/RadialProgress/IProgressSystemProvider.cs) | Reuse the renderer, shaders, and progress ownership boundary. |
| Entity hold interaction | [EntityAttachedContainerAutoStashClient](../VanillaExpanded/src/AutoStashing/EntityAttachedContainerAutoStashClient.cs) | Extract timing, progress ownership, cancellation, and submit-once state into a shared component. |
| Block hold interaction | [BlockBehaviorAutoStashable](../VanillaExpanded/src/AutoStashing/BlockBehaviorAutoStashable.cs) | Adapt to the shared lifecycle where practical, preserving block-specific handling and the post-stash grace period. |
| Slot movement | [InventoryTransferExecutor](../VanillaExpanded/src/AutoStashing/Transfers/InventoryTransferExecutor.cs) | Reuse engine-owned slot transfers; relocate feature-neutral transfer types into a shared inventory namespace. |
| Inventory session ownership | [AutoStashInventorySession](../VanillaExpanded/src/AutoStashing/Targets/AutoStashInventorySession.cs) | Generalize and reuse if corpse transfers require a session, preserving already-open inventories. |
| Stash orchestration | [AutoStashService](../VanillaExpanded/src/AutoStashing/AutoStashService.cs) | Retain as stash-specific orchestration rather than reversing it for looting. |

### Shared Hold Lifecycle

Keep the shared component small. It should own:

- Elapsed hold time and configured completion delay.
- Progress visibility after the grace period.
- Progress percentage and label.
- Cancellation and progress cleanup.
- Completion state, preventing repeated submission during one hold.

Feature adapters should continue to own input interpretation, target identity,
eligibility checks, and request dispatch. Preserve the existing distinction
between block interaction callbacks and the attached-container tick-driven
gesture; do not require a single input mechanism.

Avoid a general-purpose interaction framework or a large configurable base
class. Extract only behavior used by AutoStash and AutoLoot.

### Shared Inventory Primitives

Use the existing engine-slot transfer boundary for actual movement. Reuse
session ownership and finalization primitives only where the corpse lifecycle
has the same requirements.

Do not move crate policies, bloomery routing, attached-bag persistence, or
matching-item selection into a generic loot engine. These remain AutoStash
concerns.

## AutoLoot-Specific Components

### Client Interaction Adapter

- Detect an eligible corpse and start the shared hold lifecycle.
- Preserve normal click-to-open behavior and vanilla harvesting interactions.
- Track the selected entity throughout the hold.
- Submit a request identifying the entity when the hold completes.
- Remove progress on cancellation, feature disablement, and disposal.

Client eligibility is advisory. It must not authorize inventory changes.

### Corpse-Loot Adapter

- Resolve the engine-owned loot inventory for a supported corpse.
- Check the applicable vanilla access and harvesting prerequisites.
- Integrate with inventory session ownership when required.
- Preserve dirty marking, synchronization, persistence, and depletion behavior.

Prefer an existing behavior/API boundary over a broad entity patch. If Harmony
is necessary, patch the narrowest verified corpse-loot interaction boundary.

### AutoLoot Service

- Resolve the live corpse inventory on the server.
- Iterate its current nonempty slots.
- Use engine destination selection to route items into eligible player storage.
- Move items directly between engine slots.
- Continue through partial transfers and other loot stacks when capacity allows.
- Bound retries when destinations reject movement.
- Report actual movement and remaining loot.

Destination selection must be limited to player storage, not unrelated open
containers, and must preserve engine slot restrictions and merge behavior.

Do not clone loot into the player inventory and then clear the corpse. Direct
slot movement preserves stack attributes, partial-transfer behavior, capacity
limits, and modification callbacks.

### Network Boundary

Add request and result packets using the existing mod channel and packet naming
conventions. The request identifies the corpse by entity ID; it does not supply
trusted item stacks, quantities, or destination slots.

Return enough information to distinguish:

- Completed collection.
- Partial collection with loot remaining.
- No available player capacity.
- Empty or unavailable target.
- Access denied.

Success feedback should follow confirmed movement, not merely client timer
completion. Expected rejection outcomes should produce appropriate feedback;
unexpected failures should use the repository's logging conventions.

## Server Authority

Validate each request against current server state:

- AutoLoot is enabled.
- The entity exists, is dead, and exposes a supported loot inventory.
- The player is within permitted interaction reach and in the same dimension.
- Applicable vanilla access restrictions and harvesting prerequisites are met.
- The inventory and available player capacity are resolved afresh.

Perform movement through the normal server execution path so concurrent looters
operate on live slots rather than independently applying stale snapshots.
Repeated or stale requests must never duplicate items.

Do not immediately despawn an emptied corpse unless the vanilla lifecycle
requires it. Preserve existing cleanup and decay behavior.

The client hold timer is a UX mechanism, not proof that the player waited.
Before implementation, decide whether the delay also needs server enforcement.
If it does, use a server-tracked start/completion interaction rather than
trusting a duration submitted by the client.

## Configuration and Integration

Recommended new settings:

| Setting | Recommended default | Purpose |
| --- | --- | --- |
| `EnableAutoLoot` | `true` | Enable corpse auto-looting. |
| `AutoLootDelay` | `0.5` seconds | Hold duration, initially matching AutoStash's default. |

The shared radial renderer currently reads `AutoStashGuiRendersTopmost`.
Pass presentation preferences into progress creation so AutoLoot does not
implicitly depend on an AutoStash setting. Preserve the existing AutoStash
setting and its behavior; decide whether AutoLoot exposes its own setting or
uses a documented default.

Wire the feature through:

- Client and server ModSystem lifecycle and feature gates.
- Packet registration in [VanillaExpandedModSystem](../VanillaExpanded/src/VanillaExpandedModSystem.cs).
- Conditional behavior/patch installation and disposal.
- Configuration properties and ConfigLib definitions.
- Localized progress text, interaction help, and result feedback.
- Feature and configuration documentation in the [README](../README.md).

AutoLoot must work when AutoStash is disabled, and vice versa.

## Delivery Plan

### Phase 1: Verify the Vanilla Contract

Confirm the supported game's corpse behavior and inventory implementation:

- How lootability differs from harvestability.
- When drops become available and whether opening an inventory generates them.
- How normal click-to-open and held interaction are dispatched.
- Which APIs enforce access and interaction reach.
- How slot changes synchronize, persist, and affect empty-corpse cleanup.

Use those findings to choose the narrow integration hook and finalize supported
corpse types.

### Phase 2: Extract Shared Primitives

Extract the hold lifecycle and relocate genuinely feature-neutral inventory
primitives. Migrate AutoStash callers without changing existing behavior.
Validate existing block and attached-container regression tests before adding
AutoLoot behavior.

### Phase 3: Implement AutoLoot

Add the corpse adapter, client interaction, server service, request/result
packets, configuration, localization, and documentation.

### Phase 4: Validate End to End

Add feature-organized tests using existing mocks and real `InventoryGeneric`
instances where appropriate. Use the existing test category conventions.

## Acceptance Criteria

### Hold and Presentation

- Normal clicks retain vanilla loot-window behavior.
- Progress appears only after the grace period and completes at the configured
  delay.
- Release, retargeting, loss of reach, disablement, and disposal clear progress.
- One continuous hold submits at most one request.
- AutoLoot and AutoStash do not leave overlapping or orphaned progress displays.

### Inventory Correctness

- With sufficient capacity, every available loot item is transferred.
- With limited capacity, only items that fit move; the remainder stays intact.
- Full inventories, restricted slots, rejected merges, and partial stack
  transfers terminate without item loss, duplication, or unbounded retries.
- Player destination selection excludes unrelated open containers.
- Already-open corpse inventories remain open after collection.
- Harvesting prerequisites and vanilla corpse cleanup remain unchanged.

### Multiplayer and Lifecycle

- Simultaneous looters cannot receive the same items.
- Missing entities, living entities, denied access, out-of-reach requests, and
  stale or repeated requests are handled safely.
- Server-confirmed outcomes drive success and partial/full-inventory feedback.
- Inventory changes synchronize correctly to the looter and other viewers.
- Feature disablement and world shutdown release listeners and progress.
- Existing AutoStash gestures, timing, and transfers remain unchanged.

## Decisions to Confirm Before Implementation

1. Whether the hold delay is purely UX or must be enforced on the server.
2. Which corpse behavior types are supported after verifying the vanilla
   lifecycle, including explicit treatment of player and modded corpses.
3. How hold-to-loot coexists with the vanilla loot window without consuming
   normal clicks or interfering with harvesting.
4. Whether AutoLoot exposes an independent render-topmost setting.

Recommended overflow policy is fixed for the first release: transfer everything
that fits and leave the rest on the corpse. Do not drop overflow into the world
or make collection all-or-nothing.
