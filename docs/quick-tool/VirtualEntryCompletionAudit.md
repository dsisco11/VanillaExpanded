# Virtual-entry prerequisite completion audit

**Verdict: both incremental investigation tasks are satisfied and may be checked.** Confidence is high for the shared-selection seam, installed offhand constraints, reference-preserving mixed movement design, and identified native notification/publication hooks. This does not certify selector extraction, production equipment execution, or live multiplayer behavior.

Independent review, 2026-09-23, using the audit-stage-completion workflow. This report supplements the original [CompletionAudit.md](CompletionAudit.md); it does not replace its historical tool-only scope. No production implementation or checklist marker was changed by this reviewer.

## Controlling documents and independent traceability

Read the revised [proposal](../../QuickToolRadialMenu.proposal.md) and [plan](../../QuickToolRadialMenu.todo), including inherited rendering, authority, identity, restoration, lifecycle, and validation constraints. The selected scope is exactly the two items under the plan's virtual-entry amendment prerequisites. Later sections explicitly own extraction, candidate-provider implementation, regression execution, equipment, and live acceptance.

Followed the proposal's Virtual Entries requirements and the [ImplementationContract.md](ImplementationContract.md#virtual-entry-amendment) addendum into [VirtualEntryContract.md](VirtualEntryContract.md), [VirtualEntryEvidence.md](VirtualEntryEvidence.md), [VerifyVirtualEntryContract.ps1](VerifyVirtualEntryContract.ps1), and [virtual-entry-probe.txt](virtual-entry-probe.txt). These relative links resolve from their containing documents. The original installed inventory evidence and commit boundary remain inherited requirements, not proof that the new offhand cases had already passed.

Independently inspected `VanillaExpanded/src/ModSystems/EquipLightSource.cs` selection methods and existing tests under `VanillaExpanded.Tests/Unit/EquipLightSource/`; adjacent `vsapi/Common/Inventory/ItemSlotOffhand.cs`; and installed IL for `EntityPlayer.LeftHandItemSlot`, `ItemSlot.CanHold/CanTake/OnItemSlotModified`, `InventoryPlayerHotbar.NewSlot/OnItemSlotModified`, and native player/hotbar publication. The original independently reviewed ItemSlot setter, InventoryBase notification, and BagInventory persistence constraints continue to apply. The proposed `VanillaExpanded/src/Lighting/LightSourceSelection.cs` is a future source location, not an existing implementation claim.

## Task mapping

| Incremental task | Status | Requirements and evidence |
| --- | --- | --- |
| Confirm side-independent shared selector extraction seam and client/server source eligibility without changing priority/ties | Satisfied | Proposal Virtual Entries requires reuse of the existing resolver, not the light hotkey's swapping code. EquipLightSource's static resolver already accepts only IPlayerInventoryManager, an offhand ItemSlot, and hand-result flags. It returns existing slots and uses common inventory/collectible data. The amendment names one shared owner, moves the resolver/brightness/detection methods together, and migrates both consumers and their tests. Direct source inspection confirms offhand before active hand, hotbar before backpack, positive LightHsv[2] recognition, and replacement only for strictly greater brightness. Server reruns the same resolver and validates ownership/runtime eligibility of its result. Full legacy enumeration is preserved; unsupported selected slots reject instead of silently choosing a different light. Shared selection is independent of the client-only shortcut lifecycle/feature flag. |
| Verify offhand validation, persistence, identity, and restoration constraints; update movement contract and review | Satisfied | Installed EntityPlayer.LeftHandItemSlot resolves the player's hotbar index 11; NewSlot makes that an ItemSlotOffhand. StorageType is Offhand, while CanHold/CanTake/OnItemSlotModified are inherited. Actual installed checks and complete reference permutations pass the six isolated scenario groups. The mixed table returns B home, moves original A into offhand while L occupies H, then returns L home before equipping C; Unequip restores A. Incompatible A rejects before assignment, including a chained selection. Empty hand, capacity, locks, and stale reference cases are covered. The contract preserves server identity/quantity checks, whole-plan validation, callback deferral, postcommit reconciliation, native owning-hotbar notifications and publication. It explicitly requires later runtime persistence/delivery verification. |

The amendment also reconciles inherited D2/D7: only the light provider adds physical offhand as an eligible source; ordinary tool-category discovery remains unchanged. `virtual:light-source` occupies fixed index 33, giving 34 outer entries. This is the explicitly documented supported-set amendment before implementation, not an inventory-driven layout change. Tool durability ranking does not leak into light selection. Generic radial-menu rendering still has no light/tool policy, retains cached combined geometry, and has no performance-testing requirement.

## Verification performed

The audit agent independently executed:

`pwsh -NoProfile -File docs/quick-tool/VerifyVirtualEntryContract.ps1`

Exit status was zero. All six result groups passed:

- Direct offhand light equip/restore retains exact A and L references.
- A -> B -> offhand L -> C -> A restores all four original locations.
- Offhand-incompatible original A rejects direct and chained light selection unchanged.
- Initially empty hand restores L to offhand and remains empty.
- Destination capacity, PutLocked, and TakeLocked reject the complete move unchanged.
- Identical-looking replacement of the offhand reference rejects without substitution.

Installed API/library versions are 1.22.7.0. API SHA256 is `034283E7E9D98EAE45EE63005576FD89BADC3C995B531CC4C3FE46F3EB2D3296`; library SHA256 is `E08F22B493B92FEAF0AAEB79D22437EA0F7EFC38AA7F72A04A47F98BC0E40DF0`, matching the retained output and original evidence.

The fixture uses actual InventoryGeneric, ItemSlotSurvival, ItemSlotOffhand, Item, and ItemStack objects. It tests actual storage/lock checks plus explicit capacity validation before assignment. Its fixed permutations are investigation cases, not a production request planner. No gameplay callbacks or packets run in this fixture. Installed IL separately shows hotbar OnItemSlotModified reaching updateSlotStatMods, the ordinary slot notification chain, and BroadcastHotbarSlot serializing Entity.LeftHandItemSlot into OffhandStack. Full player-inventory publication remains the previously identified public BroadcastPlayerData(true) path.

## Discrepancies reconciled against current scope

| Observation | Classification | Evidence and disposition | Action |
| --- | --- | --- | --- |
| Shared selector file and production provider do not yet exist | Expected or intended | The selected task confirms an extraction seam; actual extraction and parity execution are explicit later checklist work. Source proves the seam without pretending a refactor occurred. | Keep extraction and production markers unchecked. |
| Legacy light resolver can enumerate special slots | Expected or intended | The proposal requires preserving its existing policy. The amendment retains enumeration and separately validates the returned source; it rejects an unsupported candidate instead of changing priority. | Implement the recorded eligibility boundary and rejection feedback later. |
| Offhand-first candidate may be unusable when original A cannot fit offhand | Expected or intended | Complete-plan rejection is explicitly permitted by Proposal / Virtual Entries. The actual slot fixture verifies this both directly and after B. Choosing another light or inventing a displaced-item destination would change approved behavior. | Retain unchanged rejection and do not bypass storage restrictions. |
| Current B can be offhand-incompatible although B -> L succeeds | Expected or intended | The full permutation returns B to its own home and puts original A in O. The fixture uses incompatible B/C and compatible A, demonstrating the actual destination edges rather than successive swaps. | Validate final edges, not an intermediate pairwise-swap sequence. |
| Fixture omits world callbacks, native saving, and packet delivery | Expected or intended; later evidence obligation | Evidence report and printed LIMIT identify these omissions. Installed IL identifies supported hooks, satisfying investigation scope without claiming execution. | Execute native persistence/publication and multiplayer acceptance during implementation validation. |
| Existing brightest-source tests include weak assertions and an outdated comment | Out of scope for this investigation; relevant to later regression work | FindBrightestLightSourceTests.MultipleLightSources only asserts positive brightness, and BrightestAtEnd only non-null. The equal-brightness test asserts the first ID. Source, rather than those weak assertions, establishes current ranking. No claim that these tests executed or fully prove parity is made. | When extracting, strengthen exact-winner assertions and remove stale commentary as part of already planned selection-parity coverage. |
| No performance test, broad build, or live game | Expected or intended | No production code changed; the user excludes performance tests. Isolated installed checks are the selected verification boundary. | No additional performance gate; retain later functional/live validation. |

No confirmed current-task issue, partially satisfied required item, or blocking evidence gap remains. The two prerequisite markers may now be completed and their pending review text updated. All extraction, production regression, rendering, equipment, native persistence execution, and multiplayer acceptance obligations remain outstanding under the later checklist sections.
