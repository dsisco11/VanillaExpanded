# AutoLoot Proposal

Status: Phase 1 integration decisions complete; implementation not started.

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

Progress cancels when the player releases Interact, closes the corpse window,
changes the active world-interaction target, moves out of reach, loses
eligibility, or disables the feature. Moving the cursor over the open loot
window does not change the bound corpse or cancel the hold. Completion initiates
one collection pass per continuous hold, potentially sending multiple vanilla
packets. Another attempt requires releasing and holding again.

Living entities and empty or otherwise ineligible corpses do not trigger
progress. AutoLoot collects already-available loot; it does not bypass a
harvesting prerequisite or generate drops.

## Phase 1 Decisions

Resolved on 2026-10-18. These decisions complete AL-01 and AL-02 in the
[implementation task list](./AutoLoot.todo). They select the implementation
contract; they do not claim a completed runtime implementation or gameplay test.

### AL-01: Interaction Hook and Input Lifecycle

Use a client-only Harmony postfix on the public virtual method
`EntityBehaviorHarvestable.OnInteract(EntityAgent, ItemSlot, Vec3d,
EnumInteractMode, ref EnumHandling)`.

The public client event APIs expose input events, but no equivalent
corpse-specific post-interaction event that establishes that this behavior
actually ran and opened its inventory. A narrow postfix supplies that boundary
without attaching new entity behaviors or intercepting all entity interactions.

- Let the original method run unchanged. Do not return a prefix result, modify
  `handled`, suppress the dialog, manufacture an open session, or replay the
  original interaction later.
- Install the patch from the client ModSystem lifecycle, not the shared
  server/client selective-patch path. Its body also checks client side, feature
  enablement, the local interacting player, interact mode rather than attack,
  and the eligible corpse/session.
- After the original returns, begin a gesture only when the normal client loot
  dialog/session is open, Interact is still held, and loot is available. Merely
  running a postfix is not evidence the original accepted the interaction.
- Advance through one client game-tick listener, using the shared hold lifecycle
  planned in Phase 2. Repeated callbacks for the same hold/target must not reset
  elapsed time or submit another collection pass.
- Observe `api.Input.MouseButton.Right` for the existing right-button gesture,
  not `InWorldMouseButton.Right`. Raw button state continues to describe the
  physical hold when a dialog captures input. Observe mouse-up without consuming
  it so a release/repress between ticks cannot be mistaken for a continuous hold.
- Keep the vanilla loot window open during progress and after collection.
  Bind the gesture to the initiating corpse ID and its exact inventory/session.
  UI cursor movement while the mouse is ungrabbed is not world retargeting.
  A subsequent world interaction with another target cancels rather than silently
  redirecting the pending pass. When mouse-grabbed world aiming is active,
  changing away from the bound target cancels.
- Recheck target existence, eligibility, session ownership, dimension, and vanilla
  interaction reach as the hold advances and immediately before collecting.
  Cancel on release, window/session closure, invalidation, feature disablement,
  focus loss, pause, or disposal. Remove progress and require a fresh eligible
  interaction before starting again.
- Gate behavior through live client configuration so enabling/disabling does not
  depend on restarting solely to install/remove this hook. Keep patch/listener
  disposal scoped so it does not remove other feature patches.

Additional installed-game inspection supporting this choice:

- `InputAPI.get_MouseButton` returns `ClientMain.MouseStateRaw`, whereas
  `get_InWorldMouseButton` returns `InWorldMouseState`.
- `ClientMain.OnMouseUpRaw` updates button state; `OnFocusChanged` clears mouse
  states, allowing the tick to cancel a hold after focus loss. Public
  `IClientEventAPI.MouseUp` provides release observation without consuming vanilla
  input; the inspected public event API does not expose a window-focus event.
- `SystemMouseInWorldInteractions.UpdateCurrentSelection` uses a mouse-coordinate
  picking ray when the mouse is not grabbed. This is why blindly comparing
  `CurrentEntitySelection` during loot-window cursor movement would incorrectly
  cancel or redirect the gesture.

The normal interaction and open packets remain responsible for server session
establishment. The client session is not a server acknowledgement; send transfers
after the normal open flow and let vanilla rejection/reconciliation remain
authoritative. Do not introduce an arbitrary readiness delay or custom handshake.

### AL-02: Eligibility, Rendering, and Feedback

First-release eligibility:

- A non-player entity that is dead and exposes the standard
  `EntityBehaviorHarvestable` corpse-loot inventory/protocol.
- `IsHarvested` is true and that inventory has at least one available loot stack.
- The local player's normal vanilla corpse interaction has opened the inventory,
  with valid reach and dimension.
- Player corpses, attached bags, generic entity containers, unharvested bodies,
  and mod-specific inventory protocols are excluded. Do not infer compatibility
  from the presence of an inventory alone. A modded creature using the unchanged
  standard behavior/protocol is not excluded solely because its asset is modded.

User-confirmed presentation:

- Always render AutoLoot progress topmost.
- Do not add `AutoLootGuiRendersTopmost` or read `AutoStashGuiRendersTopmost` for
  AutoLoot. Supply the fixed preference through shared progress creation while
  preserving AutoStash's existing configurable preference.
- Keep the corpse loot window visible and bind progress to that corpse rather
  than interpreting movement over the window as a new world target.

Feedback contract:

- Show the localized progress label while holding; remove the indicator on
  cancellation or after initiating the single collection pass.
- Do not play an item-collected success sound, claim a confirmed item count, or
  announce "all looted" based only on timer completion or predicted movement.
- Initially use the existing loot and player inventories to display remaining
  items/capacity; no new aggregate completion, full-inventory, or access-denied
  notification is required. In particular, a zero predicted move is not proof of
  an access denial or server-confirmed full inventory.
- Let vanilla authoritative updates/corrections reconcile contents without
  automatic resubmission. Unexpected failures use standard logging.

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
- Track the bound corpse/session throughout the hold without treating movement
  over the open loot window as world retargeting.
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

Use the client-only `EntityBehaviorHarvestable.OnInteract` postfix selected in
Phase 1 rather than a broad entity patch.

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

The client additionally cancels its gesture on target invalidation, a new
world-interaction target, loss of reach, or a dimension change. UI-only cursor
movement does not invalidate the bound target. Do not assume every movement
packet independently rechecks every condition of the original interaction.

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
implicitly depend on an AutoStash setting. AutoLoot always supplies topmost=true,
with no additional setting. Preserve the existing AutoStash setting and its
behavior.

Wire the feature through:

- Client ModSystem lifecycle and feature gates.
- Client-only corpse interaction postfix installation and scoped disposal.
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

### Phase 1: Resolve Integration Details (Complete)

AL-01 and AL-02 are resolved in the Phase 1 Decisions section above: use the
client-only corpse interaction postfix, raw held-button state, a bound
corpse/session while its window is open, standard harvested-corpse eligibility,
fixed topmost rendering, and neutral prediction-aware feedback.

In-game checks are acceptance tests after implementation, not prerequisites to
starting implementation or another feasibility investigation.

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
- Release, world retargeting, session closure, loss of reach, focus loss,
  disablement, and disposal clear progress.
- Cursor movement over the open loot window does not cancel or retarget the hold.
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

## Deferred Scope

Support for player corpses or additional mod-specific corpse protocols is not
part of this implementation. Consider it separately only after verifying the
relevant interaction, access, and packet contracts.

Recommended overflow policy is fixed for the first release: transfer everything
that fits and leave the rest on the corpse. Do not drop overflow into the world
or make collection all-or-nothing.
