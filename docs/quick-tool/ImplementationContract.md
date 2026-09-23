# Quick-Tool Implementation Contract

Planning decision and API investigation record, 2026-09-23.

Authority: [approved proposal](../../QuickToolRadialMenu.proposal.md), [implementation checklist](../../QuickToolRadialMenu.todo), and the user's subsequent lower-durability and cached-mesh rendering decisions. This record resolves implementation choices under that approval; it does not claim that the feature is implemented. No performance testing is required.

Review status: Installed-contract probe, [second review](SecondReview.md), and [independent completion audit](CompletionAudit.md) passed on 2026-09-23. The original phase 1 contract and virtual-entry amendment prerequisites are complete. The amendment passed its separate [independent audit](VirtualEntryCompletionAudit.md) after the [focused contract and second review](VirtualEntryContract.md). Production implementation remains unstarted.

## Evidence boundaries

Repository inspected at `d08f491a169a0cd3a102544dea399554ee1705cb`; adjacent vsapi source at `63d33f70018c606fb0f70ead900fe90625fbd7b2`. Project references resolve through `VINTAGE_STORY`. The [installed evidence](InventoryContractEvidence.md) and [raw probe results](installed-api-probe.txt) identify API/library 1.22.7.0, SHA256 hashes, inspected signatures, and passing isolated validation results. The reproducible [probe](VerifyInstalledInventoryContract.ps1) was run by the validation subagent; it is not production movement code. Adjacent source explains contracts but is not silently treated as installed runtime proof.

This is a design and integration investigation. Live GUI rendering, mouse routing, package loading, and multiplayer execution remain later implementation acceptance obligations. An isolated installed-API probe establishes primitive behavior, not end-to-end game validation.

## Resolved decisions

| ID | Decision | Basis and effect |
| --- | --- | --- |
| D1 | Rank by highest `GetToolTier(slot)`, then lowest positive `GetRemainingDurability(stack)`, then hotbar before backpack and ascending slot index. | Retains proposed tier precedence and applies the user's confirmed worn-tool preference. Use `GetTool(slot)`, not just the raw `Tool` field. For `GetMaxDurability(stack) <= 0`, treat the tool as non-wearing and rank its durability as infinity within its tier; do not confuse it with a broken durable tool. Durable tools with remaining durability <= 0 are unavailable. Unknown categories or failing/non-comparable metadata are skipped with diagnostic feedback, not assigned a fabricated category. |
| D2 | Search the player's own hotbar normal hand/inventory slots and backpack content slots. Include modded tools returning a supported `EnumTool`. | For tool-category candidates, exclude offhand/equipment, bag-equipping slots, mouse/crafting/creative inventories, and opened external containers. Do not enumerate every inventory in `Inventories` as personal storage. Compatibility for custom inventory/slot types requires their movement/persistence contract to be established; unsupported slots are disabled rather than mutated unsafely. |
| D3 | Preserve the item held before the first quick-tool selection across chained selections. | Adopt the proposal's A-to-B-to-C-to-A example. No stack splitting or merging; move whole stacks, including the original non-tool item. |
| D4 | Manual active-hotbar changes or replacement of the held item invalidate the session without moving anything. | Preserve subsequent player intent. Durability changes on the same server stack reference do not constitute replacement; broken/removed stacks do. |
| D5 | If the recorded return slot is unavailable, use the first compatible empty eligible slot in deterministic hotbar/backpack order. | Preserve unrelated items. Validate the complete arrangement, including restoration of A, before moving anything. No valid complete arrangement means rejection without mutation. |
| D6 | Disable Unequip without a valid quick-tool session. | The original feature restores a temporary selection. Manually equipped tool removal is outside this implementation. Selecting the tool already held is a no-op and creates no session. |
| D7 | Use the fixed named category table below, clockwise, with wedge 0 centered at 12 o'clock. | No inventory-dependent compaction, sorting, or resizing. Disabled categories keep their positions. This chooses a concrete initial layout without adding a favorites/layout editor. |

These are implementation decisions, not requests for renewed permission. They replace the corresponding unresolved questions in the source proposal. Any future change must update this record, the proposal, and affected checklist requirements together.

### Canonical category mapping

The tool-category portion covers the installed `EnumTool` category set explicitly, including unused categories as disabled wedges. The virtual-entry amendment appends Light source at index 33; the amended outer layout has 34 entries. Tool indices 0-32 remain as listed, with angles evaluated using N=34. Mapping uses stable names rather than relying on enum iteration order or translated labels. For N entries, wedge i has clockwise center angle `i * 360/N` from screen-up and boundaries half a wedge to either side. Center circle uses the separate identifier `unequip`.

| Index | Category | Index | Category | Index | Category |
| --- | --- | --- | --- | --- | --- |
| 0 | Knife | 11 | Saw | 22 | Pike |
| 1 | Pickaxe | 12 | Chisel | 23 | Shield |
| 2 | Axe | 13 | Scythe | 24 | Club |
| 3 | Sword | 14 | Sling | 25 | Mace |
| 4 | Shovel | 15 | Wrench | 26 | Warhammer |
| 5 | Hammer | 16 | Probe | 27 | Poleaxe |
| 6 | Spear | 17 | Meter | 28 | Halberd |
| 7 | Bow | 18 | Drill | 29 | Polearm |
| 8 | Shears | 19 | Firearm | 30 | Staff |
| 9 | Sickle | 20 | Crossbow | 31 | Tongs |
| 10 | Hoe | 21 | Javelin | 32 | Crowbar |

A modded item using an existing category takes that category's wedge. Unknown numeric enum values are unsupported, not dynamically appended. A future supported-category extension requires an explicit versioned mapping change before opening the menu; it may change geometry because the supported set changed. Inventory discovery never changes the set. Visual readability of this many wedges is an explicit rendering acceptance case; it is not permission to silently omit disabled positions.

## Functionality and lifecycle integration

Use two independently owned systems: a client radial-menu system and a quick-tool system with client input/cache responsibilities and a server inventory authority. Register any quick-tool request/result message types on both sides in the existing `VanillaExpandedModSystem.Start` channel registration; handlers and policy remain in the quick-tool domain. Follow `AlloyDepositSystem`'s request/result ownership pattern without copying its rollback assumptions.

Add a feature enable flag following `VanillaExpandedConfig` conventions and consume live changes through `ILiveConfigurable.OnConfigReloaded`. Load the systems independently of the initial flag so re-enable works. The server's flag gates every movement request; the client flag gates opening. Disablement closes the menu, cancels unsent work, unsubscribes caches, and clears session metadata without unequipping. An already committed server operation remains committed. Do not unload/unpatch unrelated features.

Initialize local inventory state on `LevelFinalize` and local `PlayerEntitySpawn`; check `PlayerReadyFired` and the presence of the local inventory manager before opening. Ignore spawn/despawn events for other players. On local replacement/despawn/death, close and discard old references. Use `LeaveWorld` and disposal as idempotent cleanup boundaries. Server session state is per player connection/world and cleared on disconnect, death/respawn, replacement, and disablement.

Localization belongs in `VanillaExpanded/assets/vanillaexpanded/lang/en.json`: keybind display name, Unequip/restore explanation, category labels, pending/unavailable and rejection messages. Use `Lang.Get` with the mod domain. Preserve the existing light-source shortcuts and behavior.

## Input contract

Use a normal focused `GuiDialog`, rather than a HUD that manually competes with world input. The quick-tool owner registers `ve.quickTool` as an inventory hotkey; choose an unbound default (`GlKeys.Unknown`) so this new feature does not take an existing shortcut. The user assigns it in Controls. The radial dialog has no automatic toggle binding; it accepts open/close commands from its owner and does not call the default toggle handler on repeated keydown.

The dialog requests `PrefersUngrabbedMouse = true`, `DisableMouseGrab = true`, and `CaptureAllInputs() = true` while it owns the interaction. Use `CaptureRawMouse()` for pointer-button selection and handle mouse down/up/move explicitly; an empty-composer dialog otherwise has no clickable bounds to consume input automatically. Selection uses the primary mouse button. Consume pointer and wheel events while open so no hotbar/world action leaks through. Restore input ownership through dialog close rather than forcing a global cursor grab state that might belong to another open dialog.

States are ClosedReady, OpenHeld, ClosedAwaitRelease, and Disabled. On a valid hotkey down in ClosedReady, refresh pending cache invalidation, snapshot the current binding, and open. Repeats in OpenHeld do not toggle; repeats in ClosedAwaitRelease cannot reopen. Click an enabled entry once, mark the event handled before closing, and enter ClosedAwaitRelease. Retain a narrow guard for the matching pointer release/world action until button-up so closing on down cannot leak the remaining click. Release of the opening binding without selection cancels. Escape or focus loss also cancels. Release observation resets the latch; losing focus alone does not simulate a new press.

Read the binding via `GetHotKeyByCode(...).CurrentMapping`; account for modifiers and any secondary key. `KeyboardKeyStateRaw` is the held-state fallback because ordinary `KeyboardKeyState` excludes input consumed by dialogs. `KeyUp`/mouse-up and `IsHotKeyPressed` can assist, but do not assume a hotkey release handler always runs after a GUI has captured the input. A render/input-lifetime update may check a few key/button flags; it does not scan inventory. If the binding changes while open, cancel and require a release/new press for the new mapping. Unsupported ambiguous button mappings must not consume a selection as a fresh opening press.

Subscribe to both client `IClientEventAPI.AfterActiveSlotChanged` and server `IServerEventAPI.AfterActiveSlotChanged`; the server event identifies the affected player. Invalidate on the event even if the player switches away and back before the next request. The server also observes held-slot `SlotModified` and invalidates when the recorded held reference/quantity is replaced, consumed, or broken, except while publishing its own committed operation. Compare the actual active slot and references again at request time because forced or silent changes may bypass an event. Displaced-item moves use deferred revalidation after the inventory operation, rather than invalidating halfway through a legitimate move.

Window-focus signal: the installed library exposes public static `Vintagestory.Client.ScreenManager.Platform` and public `ClientPlatformAbstract.IsFocused`. Read this through one version-specific adapter while the menu is active and before selection; cancel if focus is false or the platform is unavailable. Dialog `UnFocus` and `PauseResume` also cancel for GUI focus and single-player pause. Avoid `RegisterOnFocusChange`: its installed registration has no verified unregister path. The retained installed evidence establishes the seam; actual OS focus behavior remains a live-runtime acceptance case.

## Inventory observation and identity

Resolve eligible inventories with `GetOwnInventory(GlobalConstants.hotBarInvClassName/backpackInvClassName)`. The installed hotbar creates an `ItemSlotSkill` at index 10 and `ItemSlotOffhand` at 11, with ordinary `ItemSlotSurvival` slots elsewhere. Filter by the validated ordinary-slot contract rather than treating every slot up to Count as a candidate or hardcoding only a numerical exclusion. Accept backpack `ItemSlotBagContent`, not bag equipment slots; require the inventory and slot implementations to match the supported adapter. Store inventory object identity and stable inventory/slot address separately. Bind each current `IInventory.SlotModified` once; detach old instances when inventories or bag-content slot objects change.

There is no assumed universal inventory-added event. On player lifecycle events and immediately before menu open/request, reconcile the two inventory references and their slot topology. Use a low-frequency client tick (250 ms) to compare those references/topology while the feature is enabled, rather than scan every item each frame. Backpack slot modification triggers topology reconciliation because replacing a bag can replace content slots even when the inventory object is unchanged. Coalesce dirty events into one scheduled refresh after the outer operation/callback completes; refresh synchronously if opening occurs before the scheduled refresh.

`CollectibleObject.DamageItem` ends by marking the slot dirty, so ordinary durability wear triggers `SlotModified`. Ranking uses `GetTool`, `GetToolTier`, `GetMaxDurability`, and `GetRemainingDurability` on current slots/stacks. Mods changing ranking-relevant state must mark the slot dirty; silent out-of-band changes cannot be inferred from the event API. The topology check does not claim to detect silent collectible metadata edits. Current candidate and server ranking are revalidated at selection; a stale candidate is rejected/refreshed rather than silently selecting another item under the pointer.

Client stack references are disposable cache hints, not cross-network identity. The server session retains the actual `ItemStack` references for A and the active tool, their whole-stack quantities, the original hand address, and the current tool's home address. It verifies inventory and slot membership on every operation. A moved recorded stack can be found only by the same reference in eligible server slots; if reference/quantity continuity is lost, invalidate rather than substitute by code or value equality. Normal durability changes are allowed; split, merged, cloned, removed, or broken stacks invalidate continuity. This requires no persistent identity attribute on game items.

Candidate requests identify a known stable entry ID (tool category or virtual provider), source inventory/slot, expected hand slot, and a bounded description of the displayed stack state for stale-selection rejection. The server resolves the actual object and current ranking; a value fingerprint is a precondition, not a claim of historical identity. Original-item restoration is protected by the server reference record. Late results from old player/world/session generations cannot restore a client session.

## Authoritative movement contract

### Boundary and request lifecycle

Use a narrow quick-tool request/result adapter on the existing reliable mod channel. Client requests express SelectEntry or Restore plus request/session generation and candidate hints; never accept a client-specified arbitrary multi-slot permutation. The server derives destinations from its own eligible inventories and restoration record. No client inventory prediction is necessary; pending UI prevents a second selection while awaiting the accepted result and inventory synchronization.

Marshal inventory work through `EnqueueMainThreadTask` if the channel does not already provide a verified main-thread boundary. Each player's operations execute serially. Validate player readiness/aliveness, feature flag, current hand, eligible source, entry-provider selection policy, stack identity, bag topology, inventory access, take/put locks, CanTake/CanHold constraints, storage tags/dimensions, and destination whole-stack capacity. Validate ALL final edges from the unchanged snapshot; no mutation is allowed during planning. Recheck identities/topology after validation hooks before committing.

Use a per-connection increasing operation sequence and a bounded response cache. Duplicate accepted sequences return the original result without moving again; evicted old sequences are rejected, never re-executed. A sequence becomes pending before scheduling. Do not trust client player identifiers; use the channel's sender. Bound all submitted strings/arrays and reject unsupported addresses. Add an EndSession control message for client-local disablement or player-context disposal; it only clears the matching session generation and cannot move items. Re-enabling starts a fresh session generation. Ignore late acknowledgements for locally ended generations; never undo an already committed move.

### Movement tables

H is the recorded active hand, S the current tool's original slot, and T the next tool's original slot. A may be an empty stack. These are whole-stack reference permutations.

| Operation | Before | After |
| --- | --- | --- |
| First equip B | H=A, S=B | H=B, S=A |
| Switch B to C | H=B, S=A, T=C | H=C, S=B, T=A |
| Restore C | H=C, T=A | H=A, T=C |
| Empty-hand restore | H=B, S=empty | H=empty, S=B |
| Restore with moved A and blocked S | H=B, U=A, S=unrelated, F=empty | H=A, U=empty, S=unrelated, F=B |

For a chain switch with moved A, plan returning B home (or to a permitted empty fallback), moving A into C's source, and moving C to H in one complete assignment map; remove vacated source contents exactly once. Deduplicate aliased addresses. If A occupies C's source because A itself is the selected tool, collapse the desired result to restoring A and closing the temporary session rather than trying to place the same reference twice. If the selected tool is already held, do nothing. Do not require an extra empty slot for the ordinary two/three-slot cycle.

### Commit and notification boundary

The existing `TryFlipItems` / `TryFlipWith` path performs a swap and invokes notifications for each call. Repeating those calls is not the selected multi-slot transaction design.

For established compatible slots, prepare the full mapping and bookkeeping first. Retain every incoming reference in the plan, then assign all final `ItemSlot.Itemstack` references within one synchronous main-thread block, with no awaits, packet sends, callbacks, or virtual calls between assignments. The installed probe establishes that this nonvirtual public setter is a plain field assignment (`02037D251200042A`); this must be rechecked when changing the supported engine version. No observer in the ordinary game-thread model can see the intermediate assignment sequence. Do not clone A/B/C for movement or merge their quantities.

After all assignments, call `slot.OnItemSlotModified(previousStackAtThisSlot)` for each affected slot, preserving the extracted-stack argument as the engine flip does. This reaches `InventoryBase.DidModifyItemSlot`: dirty marking, inventory-specific `OnItemSlotModified`, `SlotModified`, and collectible callbacks. The installed backpack callback calls `BagInventory.SaveSlotIntoBag` for content slots; bare `MarkSlotDirty` alone is insufficient. Use public `IServerPlayer.BroadcastPlayerData(true)` to publish the authoritative player inventories, and `IPlayerInventoryManager.BroadcastHotbarSlot()` for the visible held item on other clients. Installed IL confirms that the former serializes player inventories through the native network utility. Its lower-level `SendInventoryContents` and `SendDirtyInventoryContents` methods are protected and must not be called as public APIs. Notifications can execute external callbacks, so the complete arrangement must already exist when they run. The installed evidence report defines the available publication surfaces; gameplay persistence execution remains an implementation test.

The commit point is completion of the assignment map, not successful completion of arbitrary later callbacks. Before commit, a rejected request leaves the inventory unchanged. After commit, a notification/send failure is CommittedNeedsReconcile, not Rejected and not grounds to restore old snapshots over callback changes. Record the committed operation before publishing callbacks; ensure every affected inventory is queued for synchronization, collect/report publication failures, and invalidate restoration if post-notification contents no longer satisfy the recorded session. Retrying the same operation only replays status/synchronization.

This guarantees the feature does not expose a half-applied permutation or duplicate/drop items under the supported game-thread and inventory contracts. It does not claim transactional rollback of arbitrary other mods' callback side effects or crash-durable transactions. Unknown inventory implementations with unverified persistence/assignment semantics must fail compatibility validation before commit. If that excludes a required standard inventory, implementation is blocked until its adapter is proven.

### Result and synchronization

Accepted results include the request sequence, current session generation/status, affected slot addresses, and authoritative final state/revision evidence sufficient to recognize completion. Use `IServerPlayer.BroadcastPlayerData(true)` for native client inventory contents and status/resync replies. This full own-inventory publication is acceptable for a user-triggered swap; no invented per-slot network packet format is needed. Do not assume a custom result packet arrives after native slot packets: keep the operation pending until the result and affected inventory updates agree, handling either arrival order. Use a status/resync request on timeout instead of guessing rejection or resending a new movement operation. A disconnected client cannot undo a committed request; reconnect starts with synchronized inventory and no transient session.

An externally changed involved slot invalidates the pending restoration record after reconciling the operation; unrelated changes do not. If reconciliation remains impossible, report unavailable and clear the client session rather than invent success. Server-side accepted/rejected and inventory synchronization integration tests remain required when the adapter is implemented.

## Ownership map

| Owner / proposed location | Responsibility and dependency |
| --- | --- |
| `src/RadialMenu/RadialMenuSystem.cs` | Thin client functionality lifecycle; owns dialog/render resources, exposes generic menu opening. |
| `src/RadialMenu/RadialMenuDialog.cs` | Pointer/keyboard capture, interaction state, generic entry selection, close lifecycle. |
| `src/RadialMenu/RadialMenuLayout.cs` | Shared geometric parameters and CPU hit testing; no inventory concepts. |
| `src/RadialMenu/RadialMenuMesh.cs` | Cached combined wedges/center with constant per-triangle entry ID and edge data. |
| `src/RadialMenu/RadialMenuRenderer.cs` and shader assets | Draw cached mesh and shader state; delegate generic icon/text drawing; reload/disposal. |
| `src/QuickTools/QuickToolSystem.cs` | Thin client/server integration for feature config, keybind, inventory subscriptions, and channel. |
| `src/QuickTools/ToolCandidates.cs` | Tool eligibility/ranking; participates in the shared entry/provider cache with virtual selectors using narrow inventory/slot inputs. |
| `src/QuickTools/ToolCategoryLayout.cs` | Canonical category table supplied to the generic menu. |
| `src/QuickTools/QuickToolSession.cs` | Server identity and restoration history; client mirrors accepted status only. |
| `src/QuickTools/QuickToolEquipment.cs` | Derive/validate/commit whole-stack assignment plan using supported inventory adapter. |
| `src/QuickTools/QuickToolNetwork.cs` | Correlation, duplicate protection, authority dispatch, result/resync ownership. Packet data lives with this domain. |

These names assign responsibilities, not a requirement to create trivial classes or wrappers. Source locations in the ownership table are relative to `VanillaExpanded/`. The implementation may combine closely coupled resource ownership where appropriate, but cannot aggregate ranking, interaction, and inventory mutation into a composition root. All new methods/classes require XML documentation and related method regions; nontrivial logic needs explanatory comments.

The shader effects and deterministic geometry remain exactly the approved design. Menu state/candidate changes never regenerate the mesh; scale changes may retessellate only to maintain the defined visual tolerance. No profiling, timing comparison, or benchmark gate is added.

## Focused verification matrix

| Boundary | Required case and evidence |
| --- | --- |
| Installed API investigation now | Verify actual assembly identity, setter IL, input/lifecycle signatures, classification/ranking methods, dirty/sync/persistence hooks; retain commands and outputs. |
| Isolated inventory fixture now | Actual installed ItemSlot references through two/three-slot cycles; observe complete state before notifications; reject restricted/full plans unchanged; distinguish postcommit notification failure from rejection. No broad feature build is necessary for a documentation/API investigation. |
| Later pure/domain tests | Candidate tier/durability/tie order, all mapping indices, no geometry rebuild on state change, radius/angle/boundary hit testing, bag-topology invalidation, identity/quantity loss. |
| Later equipment tests | A-B-A, A-B-C-A, empty hand, selecting original A, aliases, duplicates, moved A/B, blocked return/fallback, restricted slots, full inventories, broken tools, notifications, duplicate/late requests. |
| Later client/server integration | Main-thread serialization, one pending request, rejection without prediction, both result/sync arrival orders, repeated status/resync, active-slot changes, disable/disconnect/reconnect. |
| Later live/runtime | Actual click-through/camera/focus behavior, shader reload, GUI scaling/readability of all fixed wedges, item rendering, world lifecycle, native bag persistence and multiplayer sync. |
| Later packaging | `build.ps1` delegates to CakeBuild; Build validates JSON then publishes, Package copies assets and creates zip. It cleans Releases, so do not invoke packaging as a casual investigation command. |

## Task-to-document traceability

All rows inherit the complete approved proposal and the selected checklist section. Relative source paths below resolve from this document. Within a cell, a basename inherits the directory of the preceding fully specified file; an API `Client/` or `Common/` path inherits the `../../../vsapi/` root. Local code shorthand inherits `../../VanillaExpanded/src/`. Adjacent checkout source is supporting API evidence; installed verification is retained in InventoryContractEvidence.md and installed-api-probe.txt. No directory reference makes every unrelated file a controlling document.

| Task | Document/path/anchor consulted | Applicable requirements | Planned / observed evidence |
| --- | --- | --- | --- |
| Resolve D1-D7 | Proposal Candidate Discovery, Equipment, Inventory Changes, Decisions; checklist Implementation decisions | Preserve best-per-category, worn-first correction, A restoration, stable wedges; resolve choices before consumers | Decision table and canonical map above; independent completion audit passed |
| Registration/config/lifecycle/localization | `../../VanillaExpanded/src/VanillaExpandedModSystem.cs` StartPre/Start; `VanillaExpandedConfig.cs`; `ModSystems/ILiveConfigurable.cs`, `LiveConfigReload.cs`, `ConfigLibIntegrationModSystem.cs`; `Constants.cs` | Two separate owners, thin roots, disable/re-enable, current config and localization conventions | Source read; integration contract above; installed lifecycle signatures in probe |
| Input and GUI ownership | `../../../vsapi/Client/API/IInputAPI.cs`, `IClientEventAPI.cs`, `ICoreClientAPI.cs`; `../../../vsapi/Server/API/IServerEventAPI.cs`; `Client/Input/HotKey.cs`, `KeyEvent.cs`, `MouseEvent.cs`; `Client/UI/Dialog/GuiDialog.cs`; local EquipLightSource and GUI examples | Hold/cancel/consume/release latch/focus cleanup; no default toggle behavior | Source sections read; installed signatures and public library focus seam verified in retained probe evidence; live behavior remains later acceptance |
| Inventory subscriptions | `../../../vsapi/Common/Entity/Player/IPlayerInventoryManager.cs`; `../../../vsapi/Common/Collectible/ItemStack.cs`; `Common/Inventory/IInventory.cs`, `InventoryBase.cs`, `BagInventory.cs`; `Common/Collectible/Collectible.cs` DamageItem/GetTool/GetToolTier/durability | Eligible own inventories, dirty batching, durability and topology updates, teardown | Source notification path identified; topology reconciliation and dirty-event policy above |
| Item identity | Proposal Temporary Equipment Session and external changes; ItemSlot/ItemStack and bag reload contracts | No code-only identity; moved/duplicate/broken/replaced stacks cannot restore wrong item | Server reference/quantity continuity and client hint boundary; passing installed-slot fixture validates actual reference preservation, stale-reference rejection, and empty-hand restoration |
| Authority/prediction/results | `../../VanillaExpanded/src/ModSystems/AlloyDepositSystem.cs`, `Network/RequestAlloyDeposit.packet.cs`, `Network/AlloyDepositResult.packet.cs`; `AlloyCalculator/AlloyDepositService.cs`; `../../../vsapi/Common/API/IEventAPI.cs` | Revalidate on server, serialize requests, distinguish commit/result/native sync | Existing source pattern read; no-prediction request/result/status contract; no live result-order claim |
| Complete movement and failure contract | `../../../vsapi/Common/Inventory/ItemSlot.cs` setter/TryFlipWith/OnItemSlotModified/MarkDirty; `InventoryBase.cs` TryFlipItems/DidModifyItemSlot; BagInventory persistence | Validate full plan before mutation, supported primitive commit, no destructive fallback or false rejection | Assignment tables; installed IL and isolated fixtures passed for A-B-A, A-B-C-A, empty hand, rejection, and postcommit notification failure; postcommit callbacks explicitly separate |
| Ownership and verification cases | Proposal System Responsibilities/Rendering/Acceptance; `../../VanillaExpanded/src/RadialProgress/` renderer/resources/system; local GUI examples; `../../VanillaExpanded.Tests/VanillaExpanded.Tests.csproj`; `../../README.md` Testing; `../../build.ps1`, `../../CakeBuild/Program.cs` | Thin owners, cached mesh/shader effects, narrow seams, meaningful verification, no performance gate | Ownership map and verification matrix; existing build/test wrappers read, not run by parent |

No production implementation, package, or live-game result is implied by this record. See [SecondReview.md](SecondReview.md) and [CompletionAudit.md](CompletionAudit.md) for the completed reviews and their explicit evidence boundaries.
## Virtual-entry amendment

The requested Light source entry extends the original audited scope. [Proposal / Virtual Entries](../../QuickToolRadialMenu.proposal.md#virtual-entries) governs this amendment; completed incremental investigation and outstanding implementation tasks are recorded in the checklist. CompletionAudit.md and its installed inventory probe remain evidence for the original contract only, not proof of offhand-source restoration or shared-selector extraction.

Use a stable identity such as `virtual:light-source` alongside `tool:Pickaxe`. Append it at index 33 after the 33 named tool categories; keep all 34 outer positions fixed regardless of availability. The center remains Unequip. This is an explicit supported-set revision before implementation, not an inventory-driven rearrangement.

A virtual provider resolves a real ItemSlot under its own selection policy, shares the candidate cache and authoritative equipment path, and supplies an ordinary generic menu entry. It does not spawn an item, mutate the tool enum, or implement separate swapping. Known entry IDs are dispatched to known providers on both sides; client data cannot install arbitrary selection behavior. If several entries resolve to the same item, retain their fixed positions and honor already-held/reference checks.

Reuse/extract `ResolveLightSourceSlot`, `TryFindBrightestLightSource`, and `IsLightSource` from `../../VanillaExpanded/src/ModSystems/EquipLightSource.cs` into a side-independent shared light-selection owner, proposed `src/Lighting/LightSourceSelection.cs` under VanillaExpanded. Keep that owner independent of both ModSystems. Existing light hotkeys continue their current equipment behavior; the new entry uses only the shared candidate selection. Current selection is offhand light, then active-hand light, then brightest hotbar, then brightest backpack; recognition/brightness uses positive Collectible.LightHsv[2], and equal brightness keeps the first enumerated slot. Do not rewrite this as global brightness sorting or apply tool-tier/worn-first rules to lights.

The virtual entry admits the player's offhand as a source in addition to existing eligible storage, specifically to preserve the current selector's priority. This does not extend ordinary tool-category discovery to offhand. Validate the resolver's returned slot against the provider's supported sources and the entire swap/return plan. Incompatible destinations reject unchanged; do not bypass offhand restrictions or silently choose a different selection policy. The focused [virtual-entry contract](VirtualEntryContract.md) specifies the shared-selector seam, offhand constraints, mixed-chain assignments, and inherited notification/persistence boundary; its installed evidence and independent review supplement the original investigation.

Observe inventory and held/offhand changes plus active-hotbar selection because they affect resolver priority even without changing stored items. Metadata changes follow the existing dirty-notification compatibility contract. Revalidate server-side through the shared selector before using cached hints. Include parity tests for hand preference, inventory priority versus absolute brightness, ties, no candidate, mixed tool/light chains, same-stack entries, and restricted offhand restoration.

Traceability: see [VirtualEntryContract.md](VirtualEntryContract.md) for the two incremental investigation tasks and their source mapping. Shared-selector extraction, regression tests, and production implementation remain later unchecked work. No performance testing or production code is added by this amendment.