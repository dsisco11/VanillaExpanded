# AutoLoot Proposal

Status: Proposed

## Summary

Add AutoLoot as a client-side convenience feature that transfers loot from an
entity corpse into the player's inventory using vanilla inventory packets.
Holding Interact displays the same radial progress indicator used by AutoStash;
completing the hold initiates collection of all available loot.

No AutoLoot code or mod installation is required on the server. The vanilla
server remains authoritative for inventory changes, just as it is when a player
manually transfers items from the corpse loot window.

Reduce duplication by sharing hold timing, progress presentation, and low-level
inventory operations. Keep corpse eligibility and loot transfer rules separate
from AutoStash's matching-item policies.

This document describes the recommended design, not an implemented feature.
Installed-game inspection confirms the vanilla packet path needed for
client-only collection. In-game validation of the hold gesture, latency, and
concurrent looting remains necessary.

## Goals

- Provide a familiar hold-to-act interaction consistent with AutoStash.
- Collect every available corpse item that fits in the player's inventory.
- Preserve vanilla harvesting, access restrictions, inventory synchronization,
  and corpse lifecycle behavior.
- Reuse existing radial rendering and inventory movement infrastructure.
- Preserve existing AutoStash gestures, timing, and transfer behavior.
- Work with an unmodified server without custom AutoLoot packets or handlers.

## Client-Only Feasibility Investigation

### Conclusion and Evidence Scope

Client-only AutoLoot is supported by the inspected vanilla inventory path for
standard harvested corpses. The feature can automate the existing loot-window
operations rather than introduce a new server operation.

Investigation date: 2026-10-18.

Evidence was obtained by inspecting local game assembly IL with Mono.Cecil:
`VSEssentials.dll` reports version `1.22.7.0`; inventory/network implementations
were inspected in the accompanying `VintagestoryLib.dll`. Public API source and
existing client inventory code in this repository were also checked.

This is implementation-level confirmation of the available API/protocol path,
not a completed in-game proof. It does not establish compatibility with every
older game version or third-party corpse implementation.

### Verified Vanilla Flow

1. `EntityBehaviorHarvestable.Initialize` creates an engine-owned
   `InventoryGeneric` named `harvestableContents-{entityId}`. It loads contents
   from the synchronized `harvestableInv` attribute and locks item insertion.
2. `EntityBehaviorHarvestable.OnInteract` requires `IsHarvested` and an
   interaction-distance check before opening the inventory through the player
   inventory manager. On the client it opens `GuiDialogCreatureContents` and
   sends the vanilla inventory-open packet.
3. `GuiDialogCreatureContents.DoSendPacket` forwards inventory operations using
   `IClientNetworkAPI.SendEntityPacketWithOffset(entityId, packetIdOffset,
   packet)`. The inspected corpse dialog uses the default offset of zero.
4. `EntityBehaviorHarvestable.OnReceivedClientPacket` accepts inventory packets
   below ID 1000 only when the player has opened that inventory and the vanilla
   entity access check succeeds. It delegates to
   `Inventory.InvNetworkUtil.HandleClientPacket`.
5. `PlayerInventoryManager.TryTransferAway` selects destination slots, predicts
   movement locally, and returns vanilla movement packets. Its
   `onlyPlayerInventory` argument can restrict destinations to player storage.
   `TryTransferTo` similarly predicts a specific slot transfer and returns its
   packet.
6. `InventoryNetworkUtil.handleMoveItemStackPacket` processes those packets on
   the server, uses the engine's `TryMoveItemStack` path, and sends inventory
   updates or corrections. AutoLoot does not need to implement these operations.
7. The corpse's existing server-side `Inv_SlotModified` callback stores the
   inventory in `harvestableInv` and marks that watched attribute dirty.
   Client `onDropsModified` reloads the inventory and updates an open dialog.
   `GuiDialogCreatureContents.OnGuiClosed` closes and synchronizes the session.

Relevant public API contracts:

- `IPlayerInventoryManager.TryTransferAway` returns packets to send for
  synchronization and supports player-only destination selection.
- `IPlayerInventoryManager.TryTransferTo` returns a vanilla transfer packet.
- `IClientNetworkAPI.SendEntityPacketWithOffset` wraps an existing packet for
  an entity's packet handler; it does not require a custom mod channel.

There is also repository prior art:
[ClientInventorySlotReconciler](../VanillaExpanded/src/AlloyCalculator/ClientInventorySlotReconciler.cs)
uses `TryTransferTo` and sends its returned vanilla packet. AutoLoot should use
the corpse dialog's entity-wrapped transport instead of copying that helper's
global `SendPacketClient` transport.

### Important Limits

- The inventory must be opened through normal vanilla interaction on both
  sides before collection. Calling `OpenInventory` only on the client is not
  sufficient authorization.
- Preserve entity-wrapped packet routing so the corpse behavior's access checks
  remain in the path. Do not replace it with global raw movement packets.
- The hold delay and feature toggle are client preferences, not server-enforced
  rules. Enforcing AutoLoot-specific timing or policy would require server code.
- There is no custom atomic "loot all" operation or aggregate result packet.
  Collection consists of ordinary transfers subject to vanilla reconciliation.
- Leave corpse persistence, synchronization, and cleanup to the existing game
  handlers. Do not write synchronized attributes or generate drops locally.

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
3. When the configured delay completes, the client initiates vanilla transfers
   from the already-open corpse inventory.
4. The vanilla server processes the transfers and synchronizes inventory state.
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
| Client slot movement | [ClientInventorySlotReconciler](../VanillaExpanded/src/AlloyCalculator/ClientInventorySlotReconciler.cs) | Reuse the vanilla prediction/packet pattern. Prefer the existing `TryTransferAway` API for player-only routing; extract a shared client helper only if both callers genuinely benefit. |
| Direct slot movement | [InventoryTransferExecutor](../VanillaExpanded/src/AutoStashing/Transfers/InventoryTransferExecutor.cs) | Keep for AutoStash's direct engine-slot execution. It does not produce the packets a client-only AutoLoot implementation needs. |
| Inventory session ownership | [AutoStashInventorySession](../VanillaExpanded/src/AutoStashing/Targets/AutoStashInventorySession.cs) | Reuse the ownership principle, not blindly the implementation. Preserve the vanilla corpse interaction/open/close protocol and already-open windows. |
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

Use the vanilla client inventory manager for predicted movement and packet
creation. A direct call to the AutoStash transfer executor is not a substitute:
local slot mutation alone does not request an authoritative server transfer.

Prefer `TryTransferAway` with `onlyPlayerInventory: true`, forwarding each
returned packet through the corpse's entity packet transport. Set
`ActingPlayer` and normal inventory-operation settings as required by the API.
If exact destination control becomes necessary, use `TryTransferTo` with the
same transport rather than implementing slot mutation manually.

Do not send both an activate/shift-click packet and the movement packets for
the same predicted action. Choose one vanilla operation path.

Do not move crate policies, bloomery routing, attached-bag persistence, or
matching-item selection into a generic loot engine. These remain AutoStash
concerns.

## AutoLoot-Specific Components

### Client Interaction Adapter

- Detect an eligible corpse and start the shared hold lifecycle.
- Preserve normal click-to-open behavior and vanilla harvesting interactions.
- Track the selected entity throughout the hold.
- Initiate vanilla inventory transfers when the hold completes.
- Remove progress on cancellation, feature disablement, and disposal.

Client eligibility is advisory. The vanilla server handles authorization and
inventory changes.

### Corpse-Loot Adapter

- Resolve the engine-owned loot inventory for a supported corpse.
- Check the applicable vanilla access and harvesting prerequisites.
- Observe normal inventory session opening and closing.
- Use the corpse dialog's entity packet transport.
- Leave dirty marking, synchronization, persistence, and depletion behavior to
  the vanilla implementation.

Prefer an existing behavior/API boundary over a broad entity patch. If Harmony
is necessary, patch the narrowest verified corpse-loot interaction boundary.

### Client AutoLoot Transfer Coordinator

- Resolve the current client view of the vanilla-opened corpse inventory.
- Iterate its current nonempty slots.
- Use engine destination selection to route items into eligible player storage.
- Let the player inventory manager predict transfers and construct packets,
  then send them through `SendEntityPacketWithOffset`.
- Continue through partial transfers and other loot stacks when capacity allows.
- Bound retries when destinations reject movement.
- Stop when the session closes or the target becomes invalid.
- Treat immediate movement counts as prediction, not server confirmation.

Destination selection must be limited to player storage, not unrelated open
containers, and must preserve engine slot restrictions and merge behavior.

Do not clone loot into the player inventory and then clear the corpse. Use
vanilla transfers to preserve stack attributes, partial-transfer behavior,
capacity limits, and modification callbacks.

### Network Boundary

Do not add an AutoLoot request packet, result packet, network channel, or server
ModSystem. Send the vanilla packets returned by the player inventory manager
through the same entity-wrapped transport used by the corpse loot dialog.

Use local state and vanilla inventory updates to provide feedback for:

- Completed collection.
- Partial collection with loot remaining.
- No available player capacity.
- Empty or unavailable target.
- Cancellation or server correction.

The vanilla protocol does not provide a feature-specific aggregate completion
or access-denied result. Do not promise an exact confirmed item count or infer
a precise rejection reason from local prediction. Completing the timer can
produce neutral interaction feedback; definitive success feedback requires
observing authoritative inventory updates.

Unexpected failures should use the repository's logging conventions.

## Vanilla Server Authority

Rely on the existing server corpse and inventory handlers rather than adding
AutoLoot validation code. The verified entity packet path checks an open session
and entity access before delegating inventory movement. The normal interaction
path checks the harvesting prerequisite and interaction distance.

The client additionally cancels its gesture on target invalidation, loss of
reach, or a dimension change. Do not assume every movement packet independently
rechecks every condition of the original interaction.

Concurrent looting and stale predictions are handled through ordinary vanilla
inventory movement and reconciliation, not an AutoLoot transaction. Validate
these cases in multiplayer before release.

Do not immediately despawn an emptied corpse unless the vanilla lifecycle
requires it. Preserve existing cleanup and decay behavior.

The client hold timer is a UX mechanism, not proof that the player waited.
Server-enforced AutoLoot timing is explicitly outside this client-only design.

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

- Client ModSystem lifecycle and feature gates.
- Client-only patch installation and disposal, if a patch is needed.
- Existing vanilla inventory and entity packet APIs; no AutoLoot packet
  registration in [VanillaExpandedModSystem](../VanillaExpanded/src/VanillaExpandedModSystem.cs).
- Configuration properties and ConfigLib definitions.
- Localized progress text, interaction help, and result feedback.
- Feature and configuration documentation in the [README](../README.md).

AutoLoot must work when AutoStash is disabled, and vice versa.

The existing [mod manifest](../VanillaExpanded/modinfo.json) already declares
`requiredOnServer: false`. Keep the mod universal for its other features; do not
change its entire side to Client merely because AutoLoot is client-only.
Verify that the new settings remain local preferences when ConfigLib is present.

## Delivery Plan

### Phase 1: Validate the Confirmed Vanilla Contract In Game

The installed-game packet and inventory path is confirmed above. Validate the
remaining interaction and runtime details:

- How lootability differs from harvestability.
- When drops become available and whether opening an inventory generates them.
- How normal click-to-open and held interaction are dispatched.
- How holding Interact continues to be observed after the loot dialog opens,
  including immersive mouse mode.
- How session opening, slot changes, and corrections behave under latency.
- How empty-corpse cleanup and session closing behave after collection.

Use those findings to choose the narrow integration hook and finalize supported
corpse types.

### Phase 2: Extract Shared Primitives

Extract the hold lifecycle. Add or extract client transfer helpers only where
existing APIs and shared callers justify them; do not relocate AutoStash's
server transfer layer solely for AutoLoot. Migrate AutoStash callers without
changing existing behavior.
Validate existing block and attached-container regression tests before adding
AutoLoot behavior.

### Phase 3: Implement AutoLoot

Add the corpse adapter, client interaction, vanilla transfer coordinator,
configuration, localization, and documentation. Do not add server handlers or
custom AutoLoot packets.

### Phase 4: Validate End to End

Add feature-organized tests using existing mocks and real `InventoryGeneric`
instances where appropriate. Use the existing test category conventions.

## Acceptance Criteria

### Hold and Presentation

- Normal clicks retain vanilla loot-window behavior.
- Progress appears only after the grace period and completes at the configured
  delay.
- Release, retargeting, loss of reach, disablement, and disposal clear progress.
- One continuous hold initiates at most one collection pass, which may send
  multiple vanilla inventory-transfer packets.
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
- Local prediction is not presented as a confirmed server result.
- Inventory changes synchronize correctly to the looter and other viewers.
- Feature disablement and world shutdown release listeners and progress.
- Existing AutoStash gestures, timing, and transfers remain unchanged.
- AutoLoot works against a server without VanillaExpanded installed.
- Packets follow the vanilla entity-wrapped corpse inventory route.
- Vanilla rejection/correction under latency does not trigger endless retries.

## Decisions to Confirm Before Implementation

1. Which additional corpse types, if any, are supported beyond the confirmed
   standard harvested-corpse path, including player and modded corpses.
2. How hold-to-loot coexists with the vanilla loot window without consuming
   normal clicks or interfering with harvesting.
3. Whether AutoLoot exposes an independent render-topmost setting.

Recommended overflow policy is fixed for the first release: transfer everything
that fits and leave the rest on the corpse. Do not drop overflow into the world
or make collection all-or-nothing.
