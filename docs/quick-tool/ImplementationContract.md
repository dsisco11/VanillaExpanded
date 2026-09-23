# Quick-Tool Implementation Contract

Planning decision and API investigation record, 2026-09-23.

Authority: [approved proposal](../../QuickToolRadialMenu.proposal.md), [implementation checklist](../../QuickToolRadialMenu.todo), and the user's subsequent lower-durability and cached-mesh rendering decisions. This record resolves implementation choices under that approval; it does not claim that the feature is implemented. No performance testing is required.

Review status: The action-time ItemStack reference lookup revision (2026-09-23) supersedes both the original server-owned design and the later callback-driven client session design. Phase 4 implementation and local verification are complete; see [current traceability](Phase4Traceability.md) and [completion audit](Phase4CompletionAudit.md). Candidate, layout, input, and installed primitive evidence remain applicable. Real multiplayer behavior remains unverified.

## Evidence boundaries

Repository inspected at `d08f491a169a0cd3a102544dea399554ee1705cb`; adjacent vsapi source at `63d33f70018c606fb0f70ead900fe90625fbd7b2`. Project references resolve through `VINTAGE_STORY`. The [installed evidence](InventoryContractEvidence.md) and [raw probe results](installed-api-probe.txt) identify API/library 1.22.7.0, SHA256 hashes, inspected signatures, and passing isolated validation results. The reproducible [probe](VerifyInstalledInventoryContract.ps1) was run by the validation subagent; it is not production movement code. Adjacent source explains contracts but is not silently treated as installed runtime proof.

This is a design and integration investigation. Live GUI rendering, mouse routing, package loading, and multiplayer execution remain later implementation acceptance obligations. An isolated installed-API probe establishes primitive behavior, not end-to-end game validation.

## Resolved decisions

| ID | Decision | Basis and effect |
| --- | --- | --- |
| D1 | Rank by highest `GetToolTier(slot)`, then lowest positive `GetRemainingDurability(stack)`, then hotbar before backpack and ascending slot index. | Retains proposed tier precedence and applies the user's confirmed worn-tool preference. Use `GetTool(slot)`, not just the raw `Tool` field. For `GetMaxDurability(stack) <= 0`, treat the tool as non-wearing and rank its durability as infinity within its tier; do not confuse it with a broken durable tool. Durable tools with remaining durability <= 0 are unavailable. Unknown categories or failing/non-comparable metadata are skipped with diagnostic feedback, not assigned a fabricated category. |
| D2 | Search the player's own hotbar normal hand/inventory slots and backpack content slots. Include modded tools returning a supported `EnumTool`. | For tool-category candidates, exclude offhand/equipment, bag-equipping slots, mouse/crafting/creative inventories, and opened external containers. Do not enumerate every inventory in `Inventories` as personal storage. Compatibility for custom inventory/slot types requires their movement/persistence contract to be established; unsupported slots are disabled rather than mutated unsafely. |
| D3 | Preserve the item held before the first quick-tool selection across chained selections. | Adopt the proposal's A-to-B-to-C-to-A example. No stack splitting or merging; move whole stacks, including the original non-tool item. |
| D4 | Manual active-hotbar changes immediately clear the session; held-stack replacement or movement out of that hand is detected at menu-open/action validation. | Find tracked ItemStack objects by reference in current eligible inventory. Normal durability changes preserve identity; missing, replaced, broken, or quantity-changed stacks fail validation. Inventory callbacks do not drive equipment-session reconciliation. |
| D5 | If the recorded return slot is unavailable, use the first compatible empty eligible slot in deterministic hotbar/backpack order. | Preserve unrelated items. Precheck a route for the intended restoration before the first native flip. If a later flip fails after an earlier one succeeds, keep the actual inventory, invalidate uncertain history, and report interruption. |
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

Use two independently owned client systems: a reusable radial-menu system and a quick-tool system with input, cache, native-swap, and restoration responsibilities. The game already owns the server handler for native inventory flip packets. Do not register quick-tool-specific request/result messages or add a quick-tool server authority.

Add a feature enable flag following `VanillaExpandedConfig` conventions and consume live changes through `ILiveConfigurable.OnConfigReloaded`. Load the systems independently of the initial flag so re-enable works. The client flag gates opening and movement. Disablement closes the menu, prevents subsequent flips, unsubscribes caches, and clears session metadata without unequipping. A native flip already sent remains subject to the game's ordinary server processing. Do not unload/unpatch unrelated features.

Initialize local inventory state on `LevelFinalize` and local `PlayerEntitySpawn`; check `PlayerReadyFired` and the presence of the local inventory manager before opening. Ignore spawn/despawn events for other players. On local replacement/despawn/death, close and discard old references. Use `LeaveWorld` and disposal as idempotent cleanup boundaries. Restoration history is client-local and never survives a world or player-context change.

Localization belongs in `VanillaExpanded/assets/vanillaexpanded/lang/en.json`: keybind display name, Unequip/restore explanation, category labels, session-unavailable and local-failure messages. Do not add pending-server-result UI. Use `Lang.Get` with the mod domain. Preserve the existing light-source shortcuts and behavior.

## Input contract

Use a normal focused `GuiDialog`, rather than a HUD that manually competes with world input. The quick-tool owner registers `ve.quickTool` as an inventory hotkey; choose an unbound default (`GlKeys.Unknown`) so this new feature does not take an existing shortcut. The user assigns it in Controls. The radial dialog has no automatic toggle binding; it accepts open/close commands from its owner and does not call the default toggle handler on repeated keydown.

The dialog requests `PrefersUngrabbedMouse = true`, `DisableMouseGrab = true`, and `CaptureAllInputs() = true` while it owns the interaction. Use `CaptureRawMouse()` for pointer-button selection and handle mouse down/up/move explicitly; an empty-composer dialog otherwise has no clickable bounds to consume input automatically. Selection uses the primary mouse button. Consume pointer and wheel events while open so no hotbar/world action leaks through. Restore input ownership through dialog close rather than forcing a global cursor grab state that might belong to another open dialog.

States are ClosedReady, OpenHeld, ClosedAwaitRelease, and Disabled. On a valid hotkey down in ClosedReady, refresh pending cache invalidation, snapshot the current binding, and open. Repeats in OpenHeld do not toggle; repeats in ClosedAwaitRelease cannot reopen. Click an enabled entry once, mark the event handled before closing, and enter ClosedAwaitRelease. Retain a narrow guard for the matching pointer release/world action until button-up so closing on down cannot leak the remaining click. Release of the opening binding without selection cancels. Escape or focus loss also cancels. Release observation resets the latch; losing focus alone does not simulate a new press.

Read the binding via `GetHotKeyByCode(...).CurrentMapping`; account for modifiers and any secondary key. `KeyboardKeyStateRaw` is the held-state fallback because ordinary `KeyboardKeyState` excludes input consumed by dialogs. `KeyUp`/mouse-up and `IsHotKeyPressed` can assist, but do not assume a hotkey release handler always runs after a GUI has captured the input. A render/input-lifetime update may check a few key/button flags; it does not scan inventory. If the binding changes while open, cancel and require a release/new press for the new mapping. Unsupported ambiguous button mappings must not consume a selection as a fresh opening press.

Subscribe to client `IClientEventAPI.AfterActiveSlotChanged` and invalidate the client restoration session on a manual hand change, even if the player switches away and back before another menu action. At menu open and before each equipment action, find tracked stacks by reference and validate that the current quick-tool still occupies the recorded active hand. Detect missing/replaced/consumed/broken stacks at these boundaries. Equipment sessions do not subscribe to `SlotModified`; those notifications remain inputs to the candidate cache.

Window-focus signal: the installed library exposes public static `Vintagestory.Client.ScreenManager.Platform` and public `ClientPlatformAbstract.IsFocused`. Read this through one version-specific adapter while the menu is active and before selection; cancel if focus is false or the platform is unavailable. Dialog `UnFocus` and `PauseResume` also cancel for GUI focus and single-player pause. Avoid `RegisterOnFocusChange`: its installed registration has no verified unregister path. The retained installed evidence establishes the seam; actual OS focus behavior remains a live-runtime acceptance case.

## Inventory observation and identity

Resolve eligible inventories with `GetOwnInventory(GlobalConstants.hotBarInvClassName/backpackInvClassName)`. The installed hotbar creates an `ItemSlotSkill` at index 10 and `ItemSlotOffhand` at 11, with ordinary `ItemSlotSurvival` slots elsewhere. Filter by the validated ordinary-slot contract rather than treating every slot up to Count as a candidate or hardcoding only a numerical exclusion. Accept backpack `ItemSlotBagContent`, not bag equipment slots; require the inventory and slot implementations to match the supported adapter. Store inventory object identity and stable inventory/slot address separately. Bind each current `IInventory.SlotModified` once; detach old instances when inventories or bag-content slot objects change.

There is no assumed universal inventory-added event. On player lifecycle events and immediately before menu open/request, reconcile the two inventory references and their slot topology. Use a low-frequency client tick (250 ms) to compare those references/topology while the feature is enabled, rather than scan every item each frame. Backpack slot modification triggers topology reconciliation because replacing a bag can replace content slots even when the inventory object is unchanged. Coalesce dirty events into one scheduled refresh after the outer operation/callback completes; refresh synchronously if opening occurs before the scheduled refresh.

`CollectibleObject.DamageItem` ends by marking the slot dirty, so ordinary durability wear triggers `SlotModified`. Ranking uses `GetTool`, `GetToolTier`, `GetMaxDurability`, and `GetRemainingDurability` on current slots/stacks. Mods changing ranking-relevant state must mark the slot dirty; silent out-of-band changes cannot be inferred from the event API. The topology check does not claim to detect silent collectible metadata edits. Revalidate the current client candidate at selection; a stale candidate is rejected/refreshed rather than silently selecting another item under the pointer.

The client session retains original and current ItemStack references, initially-empty-hand state, the original active hotbar position, and the current quick-tool's preferred home address. Quantities and selected entry are validation metadata. Remove expected displaced-item locations. Search eligible inventory for each exact reference on menu open and before switch/restore; each required object must occur once. Resolve source slots from that search and validate the home as a destination independently. A moved original stack remains usable wherever found; a current quick-tool outside the recorded hand ends the session. Missing/replaced references, changed whole-stack quantities, and unusable items end restoration without substitution. Normal durability wear preserves identity. No serialized-content rebinding or persistent item ID is introduced.

Native accepted updates and rejected-request corrections can change the client inventory and replace stack objects. Their arrival does not classify a quick-tool request. If a tracked object is replaced, the next lookup cannot find it and restoration ends, even when equal contents remain. Verify the frequency and usability impact of this limitation in multiplayer acceptance. The inventory subscriptions and topology tick described above belong to candidate-cache maintenance; they do not introduce equipment-session callback state.

Selection accepts only a known stable entry ID and reruns its current provider on the client. The cached candidate must still match the current slot and active hand before the first flip. The game validates each resulting native flip packet on the server; the quick-tool client never sends arbitrary stack contents. Discard local restoration state on world/player replacement and ignore old-context observations.

## Client-owned native movement contract

### Boundary and operation lifetime

The quick-tool owner runs on the client. It sends the ordinary inventory flip packet returned by `IInventory.TryFlipItems` for each swap, as the existing light shortcut does. The server's native inventory handler remains authoritative for each packet. Do not add a quick-tool packet handler, server restoration session, or custom result protocol. A selection across time does not hold a server transaction open.

Before the first flip, verify the current player, active hand, known entry provider, current winner, eligible source, relevant slot topology, stack quantities, and intended return route. Select only ordinary owned hotbar/backpack slots, plus physical offhand as a source for Light. The client checks the next source and destination immediately before every native flip. `TryFlipItems` applies its own `TryFlipWith` restrictions and returns a packet only on local success; send exactly that packet. Do not fabricate a native packet or assign `ItemSlot.Itemstack` directly. A provider result from an unsupported slot is unavailable.

Guard synchronous local flips against reentrant equipment calls. Each native flip mutates the client view immediately, so validate its actual local result and permit another action once the sequence ends. That next action performs fresh reference lookup. There is no pending server-result, slot-callback, or timeout gate. On world exit, player replacement, manual active-slot change, or feature disablement, clear the session and stop unsent flips. A sent flip remains subject to normal game synchronization.

### Movement examples and swap order

H is the recorded active hand, S the current tool's home, T the next candidate's source, U the current location of the original item A, and F a compatible empty fallback. A may be empty.

| Operation | Initial slots | Native flips | Intended result |
| --- | --- | --- | --- |
| First equip B | H=A, S=B | H with S | H=B, S=A |
| Switch B to C | H=B, S=A, T=C | H with S; H with T | H=C, S=B, T=A |
| Restore C | H=C, T=A | H with T | H=A, T=C |
| Empty-hand restore | H=B, S=empty | H with S | H=empty, S=B |
| Restore after moved A and blocked S | H=B, U=A, S=unrelated, F=empty | H with F; H with U | H=A, U=empty, S=unrelated, F=B |

When A has moved away from S, use a supported empty return slot for B, then swap A into H before selecting C. The next selection may add a final H-with-T flip. If A is the selected candidate, restore it and close the session; an already-held candidate is a no-op. Validate the intended final arrangement before starting, but do not claim that this makes several native flips atomic. Recheck after each flip. If an intermediate operation fails, preserve the actual resulting inventory, stop, invalidate uncertain history, and report interruption. Never issue speculative rollback packets over state that another handler may have changed.

### Native notifications and action-time validation

`TryFlipItems` calls `TryFlipWith`, which checks each pair's capacity and hold/take rules and invokes normal slot modification callbacks. The returned native flip packet is the game's supported synchronization path. The prior installed probe's direct setter and manual notification findings remain historical API evidence; production quick-tool movement does not use that path.

The local inventory has already changed when `TryFlipItems` returns. Verify the resulting contents immediately and update session references/home after the sequence. Inventory events refresh candidates; they do not reconcile the equipment session or infer acceptance/rejection. Later accepted updates and corrections are handled by the game, and their effects are validated by the next reference lookup. Missing references end restoration without moving items or matching equal contents.

No acknowledgement is required for the next client action. Native synchronization behavior for accepted and rejected flips, including object replacement when predicted contents already match, is a multiplayer acceptance obligation. Report local outcomes accurately and document reference-loss limitations without treating local completion as server confirmation.

## Ownership map

| Owner / proposed location | Responsibility and dependency |
| --- | --- |
| `src/RadialMenu/RadialMenuSystem.cs` | Thin client functionality lifecycle; owns dialog/render resources, exposes generic menu opening. |
| `src/RadialMenu/RadialMenuDialog.cs` | Pointer/keyboard capture, interaction state, generic entry selection, close lifecycle. |
| `src/RadialMenu/RadialMenuLayout.cs` | Shared geometric parameters and CPU hit testing; no inventory concepts. |
| `src/RadialMenu/RadialMenuMesh.cs` | Cached combined wedges/center with constant per-triangle entry ID and edge data. |
| `src/RadialMenu/RadialMenuRenderer.cs` and shader assets | Draw cached mesh and shader state; delegate generic icon/text drawing; reload/disposal. |
| `src/QuickTools/QuickToolSystem.cs` | Thin client integration for feature config, keybind, inventory subscriptions, and native inventory packets. |
| `src/QuickTools/ToolCandidates.cs` | Tool eligibility/ranking; participates in the shared entry/provider cache with virtual selectors using narrow inventory/slot inputs. |
| `src/QuickTools/ToolCategoryLayout.cs` | Canonical category table supplied to the generic menu. |
| `src/QuickTools/QuickToolSession.cs` | Tracked ItemStack references, active hand position, return destination, and validation metadata. |
| `src/QuickTools/QuickToolClientOperations.cs` | Current player context, session lifecycle, and synchronous reentrancy handling; equipment state has no inventory callback subscriptions or pending-response state. |
| `src/QuickTools/QuickToolEquipment.cs` | Validate intended destinations, execute supported native flips in order, and stop on interruption. |

These names assign responsibilities, not a requirement to create trivial classes or wrappers. Source locations in the ownership table are relative to `VanillaExpanded/`. The implementation may combine closely coupled resource ownership where appropriate, but cannot aggregate ranking, interaction, and inventory mutation into a composition root. All new methods/classes require XML documentation and related method regions; nontrivial logic needs explanatory comments.

The shader effects and deterministic geometry remain exactly the approved design. Menu state/candidate changes never regenerate the mesh; scale changes may retessellate only to maintain the defined visual tolerance. No profiling, timing comparison, or benchmark gate is added.

## Focused verification matrix

| Boundary | Required case and evidence |
| --- | --- |
| Installed API investigation now | Verify actual assembly identity, setter IL, input/lifecycle signatures, classification/ranking methods, dirty/sync/persistence hooks; retain commands and outputs. |
| Historical isolated inventory fixture | Direct-assignment tests established installed slot primitives for the superseded design; they do not validate native multi-flip behavior. |
| Later pure/domain tests | Candidate tier/durability/tie order, all mapping indices, no geometry rebuild on state change, radius/angle/boundary hit testing, bag-topology invalidation, identity/quantity loss. |
| Client equipment tests | A-B-A, A-B-C-A, empty hand, selecting original A, aliases, duplicates, moved A/B, blocked return/fallback, restricted slots, full inventories, broken tools, exact native packet sequence, and an interrupted second flip. |
| Native inventory integration | Immediate successive local actions, later accepted updates/corrections followed by reference lookup, object replacement, active-slot changes, disable/disconnect/reconnect, and ambiguous references. No response or slot-callback gate is permitted. |
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
| Item identity | Proposal Temporary Equipment Session and external changes; ItemSlot/ItemStack and bag reload contracts | Find current ItemStack references on menu open/action; missing, ambiguous, broken, or replaced objects cannot restore the wrong item | Current local reference-lookup tests pass; live native object-replacement behavior remains unverified |
| Native movement | `../../VanillaExpanded/src/ModSystems/EquipLightSource.cs` flip/send path; `../../../vsapi/Common/Inventory/ItemSlot.cs` TryFlipWith; `InventoryBase.cs` TryFlipItems | Rerun local provider, send only native flip packets, validate local results | Current exact-sequence and immediate-action fixtures pass; live server acceptance remains unverified |
| Sequential failure contract | Proposal Switching, Unequip, and Authority; ItemSlot/InventoryBase native flip behavior | Precheck route, recheck each flip, preserve actual partial state, no invented rollback | Current interruption, changed-source, and lifecycle-clear fixtures pass; historical direct-assignment evidence is superseded |
| Ownership and verification cases | Proposal System Responsibilities/Rendering/Acceptance; `../../VanillaExpanded/src/RadialProgress/` renderer/resources/system; local GUI examples; `../../VanillaExpanded.Tests/VanillaExpanded.Tests.csproj`; `../../README.md` Testing; `../../build.ps1`, `../../CakeBuild/Program.cs` | Thin owners, cached mesh/shader effects, narrow seams, meaningful verification, no performance gate | Ownership map and verification matrix; existing build/test wrappers read, not run by parent |

The earlier [second review](SecondReview.md) and [completion audit](CompletionAudit.md) apply to the original investigation. The revised implementation is reviewed in [Phase4SecondReview.md](Phase4SecondReview.md) and [Phase4CompletionAudit.md](Phase4CompletionAudit.md). Native multiplayer evidence remains required.
## Virtual-entry amendment

The requested Light source entry extends the original audited scope. [Proposal / Virtual Entries](../../QuickToolRadialMenu.proposal.md#virtual-entries) governs this amendment; completed incremental investigation and outstanding implementation tasks are recorded in the checklist. CompletionAudit.md and its installed inventory probe remain evidence for the original contract only, not proof of offhand-source restoration or shared-selector extraction.

Use a stable identity such as `virtual:light-source` alongside `tool:Pickaxe`. Append it at index 33 after the 33 named tool categories; keep all 34 outer positions fixed regardless of availability. The center remains Unequip. This is an explicit supported-set revision before implementation, not an inventory-driven rearrangement.

A virtual provider resolves a real ItemSlot under its own selection policy, shares the candidate cache and client native-swap path, and supplies an ordinary generic menu entry. It does not spawn an item, mutate the tool enum, or implement separate swapping. Known entry IDs are dispatched to known client providers; no quick-tool-specific server request is sent. If several entries resolve to the same item, retain their fixed positions and honor already-held/identity checks.

Reuse/extract `ResolveLightSourceSlot`, `TryFindBrightestLightSource`, and `IsLightSource` from `../../VanillaExpanded/src/ModSystems/EquipLightSource.cs` into a side-independent shared light-selection owner, proposed `src/Lighting/LightSourceSelection.cs` under VanillaExpanded. Keep that owner independent of both ModSystems. Existing light hotkeys continue their current equipment behavior; the new entry uses only the shared candidate selection. Current selection is offhand light, then active-hand light, then brightest hotbar, then brightest backpack; recognition/brightness uses positive Collectible.LightHsv[2], and equal brightness keeps the first enumerated slot. Do not rewrite this as global brightness sorting or apply tool-tier/worn-first rules to lights.

The virtual entry admits the player's offhand as a source in addition to existing eligible storage, specifically to preserve the current selector's priority. This does not extend ordinary tool-category discovery to offhand. Validate the resolver's returned slot against the provider's supported sources and the entire swap/return plan. Incompatible destinations reject unchanged; do not bypass offhand restrictions or silently choose a different selection policy. The focused [virtual-entry contract](VirtualEntryContract.md) specifies the shared-selector seam, offhand constraints, mixed-chain assignments, and inherited notification/persistence boundary; its installed evidence and independent review supplement the original investigation.

Observe inventory and held/offhand changes plus active-hotbar selection because they affect resolver priority even without changing stored items. Metadata changes follow the existing dirty-notification compatibility contract. Rerun the shared selector client-side before using cached hints; the game's native inventory handler validates each flip. Include parity tests for hand preference, inventory priority versus absolute brightness, ties, no candidate, mixed tool/light chains, same-stack entries, and restricted offhand restoration.

Traceability: see [VirtualEntryContract.md](VirtualEntryContract.md) for the two incremental investigation tasks and their source mapping. Shared-selector extraction, regression tests, and production implementation remain later unchecked work. No performance testing or production code is added by this amendment.
