# AutoStash Functional Test Coverage Evidence

## Scope and controlling documents

Selected contract: [FunctionalTestCoverage.todo](FunctionalTestCoverage.todo), Phase 1 and inherited Scope and execution rules. Source audit: the eight findings in the conversation dated 2026-10-01, reproduced as the tasklist's audit cross-reference. Related [PlanningAndExecutionProposal.md](PlanningAndExecutionProposal.md) was read completely: its engine ownership, lifecycle distinctions, source ordering and evidence limits are relevant context. Its proposed architecture and stronger failure-persistence guarantees are outside this implementation.

The complete-todo-phase and audit-stage-completion skills were read directly from C:/Users/Sisco/.codex/skills. They require linked-document traceability, second review, an independent audit, and checking markers only after verification. No unavailable controlling documents were identified. The proposed evidence document did not previously exist; this file fulfills its creation task.

## Baseline

Branch: qol-improvements. Clean starting revision: 9434f660ea22f79e9fd47f7384ad128f7fd94a20. Baseline ran from a clean HEAD archive because a concurrent helper addition caused a compile error in the working copy. The archive contains the unchanged tracked source at this exact revision.

Command: dotnet test VanillaExpanded.Tests/VanillaExpanded.Tests.csproj --filter FullyQualifiedName~AutoStash --verbosity minimal.

Observed baseline: 96 passed, 0 failed, 0 skipped. Restore initially failed with NU1301 (sandbox network); approved escalation recovered restore. Compiler warnings: 34 existing warnings (7 production, 27 tests), including AutoStash BlockBehaviorAutoStashable.cs CS8602, BloomeryInteractionHelpTests.cs CS8604, and AutoStashTransferTests.cs CS8602.

Engine path: G:/Vintagestory. Assembly versions: VintagestoryAPI, VSSurvivalMod, VSEssentials and VintagestoryLib all 1.22.7.0. API file/product version 1.22.0; other listed engine assemblies file/product 1.22.7. The test project targets net10.0 and resolves these installed assemblies.

## Task-to-document traceability and implemented evidence

Every F1 item inherits FunctionalTestCoverage.todo#scope-and-execution-rules and #phase-1-establish-fixtures-and-strengthen-existing-assertions. Every item also inherits the contextual proposal's Scope, Incremental planning and execution, Target lifecycle and partial failure, and Evidence limits. The governing requirements are test-only changes, real engine mutation, precise contents/lifecycle assertions, cleanup, and review before completion.

| Task | Document/source requirement | Implementation and observed evidence |
| --- | --- | --- |
| F1.1 | Tasklist baseline; proposal evidence limits | Revision/assemblies/command/count/warnings recorded above; clean baseline executed by phase1_tests subagent. |
| F1.2 | Tasklist evidence mapping | This traceability table and complete scenario register below; future work remains planned. |
| F1.3 | Tasklist exact state assertions; proposal engine ownership | Support/InventorySnapshot.cs captures slot/collectible identities, quantities, serialized persistent and temporary attributes, and item-class/code totals. Snapshot_DetectsMutationWithoutAliasingExpectedState and ObservedSlot_ControlsBoundaryAndRecordsActualEngineMoves validate its sensitivity and conservation. |
| F1.4 | Audit finding 5; tasklist exact destination/source assertions | Existing generic backpack/hotbar/combined/multiple-type and crate success tests now assert exact merged destinations, conserved totals, unaffected slots, and session/dirty effects. |
| F1.5 | Audit finding 5; tasklist no-op assertions | Existing empty/no-match/empty-player/no-exception generic/crate tests and shared no-capacity/full eligibility checks capture unchanged inventories and no dirty/session effects. |
| F1.6 | Audit finding 5; proposal matching eligibility versus compatibility | Existing merge fixtures use the same collectible instances; GenericContainer_CodeOnlyEligibility_UsesEmptySlotWithoutReplacingExistingIdentity separately uses distinct IDs with shared code, a full existing stack, and an empty destination. |
| F1.7 | Audit finding 5; proposal client bloomery gate distinction | Isolated noncombustible/low-temperature negatives seed valid existing fuel so they reach source classification. Burning/output negatives seed matching existing fuel. Other negative setups reviewed against prerequisites; mixed-valid-invalid tests already pass the gate. |
| F1.8 | Audit finding 5 fixture realism; proposal crate behavior preservation | Support/InitializedCrate.cs invokes engine BlockEntityCrate.InitInventory. Source ../vssurvivalmod/BlockEntity/BECrate.cs InitInventory shows InventoryGeneric, suitability/automation callbacks, retrieve-only PutLocked, and slot-modified notifications. InitializedCrate_UsesVanillaInventoryConfiguration covers unlocked success and retrieve-only rejection with actual initialized slots. Full block lifecycle/Harmony checks belong to F8.7. |
| F1.9 | Tasklist deterministic observation; proposal engine ownership | ObservedTransferSlot controls zero/limited movement and callbacks with a finite attempt guard while delegating actual movement; AutoStashObservation records lifecycle/persistence and tick callbacks with reverse cleanup; ObservedInventory records engine suitability. Fixture tests validate movement, rejection, exceptions, bounded observation, tick delivery, cleanup and engine ranking. These helpers do not simulate the AutoStash loop. |
| F1.10 | Tasklist isolation/cleanup | AutoStashTestCollection disables parallelization against other collections; existing AutoStash classes join it. Config-dependent classes dispose AutoStashTestScope after each test. ConfigurationScope_RestoresSettingsAfterException validates restoration. Observation_CleansAllResourcesAfterFailure validates all cleanup actions execute even when one fails. No actual Harmony patches or game listeners are installed by this work; future callers can register their exact release action with AutoStashObservation. |

## Test/source locations

Support files and AutoStashFixtureTests are under VanillaExpanded.Tests/Unit/AutoStashing. Existing assertion changes are in AutoStashTransferTests and GetStashableItemsBloomeryTests. Other existing AutoStash test files add collection membership and, where settings are read, a disposable configuration scope. No production files were changed.

## Validation and review

Initial fixture compilation issues (attribute serialization interface and synchronous assertion overload) were corrected. The first initialized-crate success fixture lacked an item code, so AutoStash correctly rejected it before transfer; setting a valid code fixed the fixture. These were test setup issues, not production defects.

Final focused command: dotnet test VanillaExpanded.Tests/VanillaExpanded.Tests.csproj --no-restore --filter FullyQualifiedName~AutoStash --verbosity minimal. Result: 104 passed, 0 failed, 0 skipped. Final repository unit command: dotnet test VanillaExpanded.Tests/VanillaExpanded.Tests.csproj --no-restore --filter Category=Unit --verbosity minimal. Result: 300 passed, 0 failed, 0 skipped. Both were run by the phase1_tests subagent.

Durable output: [AutoStash](evidence/FunctionalTestCoverage.Phase1.AutoStash.txt), [Unit](evidence/FunctionalTestCoverage.Phase1.Unit.txt), and [baseline receipt](evidence/FunctionalTestCoverage.Phase1.Baseline.txt). Baseline receipt is reconstructed from the recorded command result, not represented as captured raw output. No new helper/fixture compiler warnings; existing test warnings remain. Independent completion audit concluded all F1.1–F1.10 requirements satisfied, with no remaining required issues or evidence gaps, after reviewing the final paired controls, exact method mapping, and refreshed logs.

The final eight additional cases are seven fixture test methods, including the initialized-crate two-row theory. Existing assertion tests retain their baseline case count. git diff --check passed (line-ending conversion notices only).

## Decisions and future scope

No behavior change was approved or implemented. Bloomery ordering, active-item/client-only gates, partial-failure persistence, preferred-slot semantics, and direct-merge termination remain the tasklist's future explicit decisions. Generic crate policy fixtures remain narrower than the new initialized-crate fixture. Engine integration and live game acceptance remain distinct; no game was launched.

## Complete scenario register

The following register maps every scenario ID to its evidence or a proposed test responsibility. Later-phase entries are deliberately unimplemented and unverified. No future markers are complete.

| ID | Contract status | Test/evidence mapping |
| --- | --- | --- |
| F1.1 | Complete; verified and independently audited | See task-to-document table above |
| F1.2 | Complete; verified and independently audited | See task-to-document table above |
| F1.3 | Complete; verified and independently audited | See task-to-document table above |
| F1.4 | Complete; verified and independently audited | See task-to-document table above |
| F1.5 | Complete; verified and independently audited | See task-to-document table above |
| F1.6 | Complete; verified and independently audited | See task-to-document table above |
| F1.7 | Complete; verified and independently audited | See task-to-document table above |
| F1.8 | Complete; verified and independently audited | See task-to-document table above |
| F1.9 | Complete; verified and independently audited | See task-to-document table above |
| F1.10 | Complete; verified and independently audited | See task-to-document table above |
| F2.1 | Planned; no completion evidence | Provisional AutoStashTransferEdgeTests scenario F2.1 (replace with actual method names during implementation): Test total target capacity below source quantity, asserting the returned moved count, exact remainder, and conservation; include zero, exact-fit, and partial capacity. |
| F2.2 | Planned; no completion evidence | Provisional AutoStashTransferEdgeTests scenario F2.2 (replace with actual method names during implementation): Test several matching source slots competing for limited capacity, with results that distinguish source-slot order and backpack-before-hotbar order. |
| F2.3 | Planned; no completion evidence | Provisional AutoStashTransferEdgeTests scenario F2.3 (replace with actual method names during implementation): Test a selected destination returning zero followed by another viable destination; assert continuation and exact final contents. |
| F2.4 | Planned; no completion evidence | Provisional AutoStashTransferEdgeTests scenario F2.4 (replace with actual method names during implementation): Test all selected destinations rejecting movement; assert finite attempts, zero result, unchanged inventories, and correct session cleanup. |
| F2.5 | Planned; no completion evidence | Provisional AutoStashTransferEdgeTests scenario F2.5 (replace with actual method names during implementation): Test automatic destinations being exhausted before deferred direct merge, and successful direct-merge retry with exact quantities and observed priorities. |
| F2.6 | Planned; no completion evidence | Provisional AutoStashTransferEdgeTests scenario F2.6 (replace with actual method names during implementation): Test rejected direct-merge retries, including a repeated request for direct priority; bound observation and verify actual termination or retain a minimal failing reproduction for explicit resolution. |
| F2.7 | Planned; no completion evidence | Provisional AutoStashTransferEdgeTests scenario F2.7 (replace with actual method names during implementation): Test source take lock, destination put lock, incompatible storage restrictions, and inventory-level rejection independently; ensure each setup reaches the restriction it intends to exercise. |
| F2.8 | Planned; no completion evidence | Provisional AutoStashTransferEdgeTests scenario F2.8 (replace with actual method names during implementation): Test same-type stacks with incompatible relevant attributes, both with an empty alternative destination and without one; assert attributes are not overwritten or incorrectly merged. |
| F2.9 | Planned; no completion evidence | Provisional AutoStashTransferEdgeTests scenario F2.9 (replace with actual method names during implementation): Test custom inventory suitability whose winning destination is not first in enumeration order; verify engine ranking is honored. |
| F2.10 | Planned; no completion evidence | Provisional AutoStashTransferEdgeTests scenario F2.10 (replace with actual method names during implementation): Test missing backpack, missing hotbar, and both missing inventories through the shared service. |
| F2.11 | Planned; no completion evidence | Provisional AutoStashTransferEdgeTests scenario F2.11 (replace with actual method names during implementation): Repeat execution after source exhaustion and after capacity exhaustion; assert no duplication, loss, extra movement, or unnecessary session acquisition. |
| F2.12 | Planned; no completion evidence | Provisional AutoStashTransferEdgeTests scenario F2.12 (replace with actual method names during implementation): Record the preferred-slot contract decision from the decisions section. If retained for characterization, cover valid/invalid indices and blocked preferred slot versus viable alternatives, explicitly labeling the current preflight/execution distinction. |
| F3.1 | Planned; no completion evidence | Provisional BloomeryTransferTests scenario F3.1 (replace with actual method names during implementation): Test ore and fuel together, asserting ore/fuel destination indices and fuel allowance based on actual deposited ore. |
| F3.2 | Planned; no completion evidence | Provisional BloomeryTransferTests scenario F3.2 (replace with actual method names during implementation): Test rounding with five ore at ratio two requiring three fuel, including existing fuel reducing the remaining allowance. |
| F3.3 | Planned; no completion evidence | Provisional BloomeryTransferTests scenario F3.3 (replace with actual method names during implementation): Test existing ore reducing ore capacity, existing fuel reducing fuel capacity, exact-full destinations, and several source stacks sharing remaining capacity. |
| F3.4 | Planned; no completion evidence | Provisional BloomeryTransferTests scenario F3.4 (replace with actual method names during implementation): Test hotbar-only ore/fuel and combined backpack/hotbar sources. |
| F3.5 | Planned; no completion evidence | Provisional BloomeryTransferTests scenario F3.5 (replace with actual method names during implementation): Add outcome-sensitive characterization of backpack ore → backpack fuel → hotbar ore → hotbar fuel. Include a distribution whose final result differs under global ore-first processing; identify the observation as current ordering rather than approving a redesign. |
| F3.6 | Planned; no completion evidence | Provisional BloomeryTransferTests scenario F3.6 (replace with actual method names during implementation): Test burning and occupied-output execution rejection, with every source and target slot unchanged. |
| F3.7 | Planned; no completion evidence | Provisional BloomeryTransferTests scenario F3.7 (replace with actual method names during implementation): Test different existing ore and fuel types, noncombustible/invalid items mixed with valid sources, and full destinations without routing to another slot. |
| F3.8 | Planned; no completion evidence | Provisional BloomeryTransferTests scenario F3.8 (replace with actual method names during implementation): Test configured `bloomeryFuelRatio` overriding the default and zero/negative configured/default ratios; assert observed engine/AutoStash interaction rather than testing an isolated copied formula. |
| F3.9 | Planned; no completion evidence | Provisional BloomeryTransferTests scenario F3.9 (replace with actual method names during implementation): Test fuel temperature immediately below/at its threshold, duration below/at/above its threshold, and ore melting point below/at minimum and below/at maximum, plus missing smelted output. |
| F3.10 | Planned; no completion evidence | Provisional BloomeryTransferTests scenario F3.10 (replace with actual method names during implementation): Verify output and unrelated player slots remain unchanged in successful mixed transfers; assert correct dirty/synchronization effects for full, partial, and zero movement. |
| F3.11 | Planned; no completion evidence | Provisional BloomeryTransferTests scenario F3.11 (replace with actual method names during implementation): Pair client eligibility and server execution tests to document the empty-bloomery active-item distinction without imposing that gate on the server. |
| F4.1 | Planned; no completion evidence | Provisional EntityAttachedContainerTransferTests scenario F4.1 (replace with actual method names during implementation): Build fixtures for vanilla `CollectibleBehaviorHeldBag` workspaces and a distinct `IHeldBag` implementation; invoke `TryAutoStash`, not only workspace helpers. |
| F4.2 | Planned; no completion evidence | Provisional EntityAttachedContainerTransferTests scenario F4.2 (replace with actual method names during implementation): For both paths, test successful matching transfers with exact source loss, target gain, persisted contents, and unrelated contents preserved. |
| F4.3 | Planned; no completion evidence | Provisional EntityAttachedContainerTransferTests scenario F4.3 (replace with actual method names during implementation): For both paths, test full bags, partial capacity, nonmatching contents, and initially empty bags. |
| F4.4 | Planned; no completion evidence | Provisional EntityAttachedContainerTransferTests scenario F4.4 (replace with actual method names during implementation): Test negative/out-of-range attachment indices, empty/non-bag attachments, zero-capacity bags, and failed vanilla workspace loading with no side effects. |
| F4.5 | Planned; no completion evidence | Provisional EntityAttachedContainerTransferTests scenario F4.5 (replace with actual method names during implementation): Assert workspace-backed session acquisition/ownership and temporary-inventory operation without session management; include already-open workspaces. |
| F4.6 | Planned; no completion evidence | Provisional EntityAttachedContainerTransferTests scenario F4.6 (replace with actual method names during implementation): Verify `IHeldBag.Store`, attachment dirty notification, and attachment-owner persistence after movement; verify no persistence for unchanged operations. |
| F4.7 | Planned; no completion evidence | Provisional EntityAttachedContainerTransferTests scenario F4.7 (replace with actual method names during implementation): Reload the actual persisted bag contents and stash again, proving no stale data, duplication, or loss. Retain workspace slot-identity assertions across repeated loads. |
| F4.8 | Planned; no completion evidence | Provisional EntityAttachedContainerTransferTests scenario F4.8 (replace with actual method names during implementation): Test candidate-only `CanAutoStash` behavior separately from execution capacity, including a matching but full bag; record the current distinction. |
| F5.1 | Planned; no completion evidence | Provisional AutoStashLifecycleTests scenario F5.1 (replace with actual method names during implementation): Inject failure after acquiring an owned inventory session and assert it closes; repeat with an already-open inventory and assert that session stays open. |
| F5.2 | Planned; no completion evidence | Provisional AutoStashLifecycleTests scenario F5.2 (replace with actual method names during implementation): Inject failure after an earlier successful move, capturing exact source/target contents, notifications, persistence, and cleanup. Establish current behavior and resolve intended persistence expectations before declaring the scenario satisfied. |
| F5.3 | Planned; no completion evidence | Provisional AutoStashLifecycleTests scenario F5.3 (replace with actual method names during implementation): Exercise a callback failure after mutation, so the fixture does not assume an unreturned operation left state unchanged. Keep reproduction and contract decision explicit if this exposes a gap. |
| F5.4 | Planned; no completion evidence | Provisional AutoStashLifecycleTests scenario F5.4 (replace with actual method names during implementation): Change target contents/capacity during session opening and verify execution observes the resulting live state without inventing reserved capacity. |
| F5.5 | Planned; no completion evidence | Provisional AutoStashLifecycleTests scenario F5.5 (replace with actual method names during implementation): Verify ordinary container/crate zero-move paths do not mark dirty, while successful partial movement produces expected notifications. |
| F5.6 | Planned; no completion evidence | Provisional AutoStashLifecycleTests scenario F5.6 (replace with actual method names during implementation): Verify bloomery and attached-bag synchronization after successful partial transfers; link rather than duplicate F3.10 and F4.6/F4.7 coverage where sufficient. |
| F5.7 | Planned; no completion evidence | Provisional AutoStashLifecycleTests scenario F5.7 (replace with actual method names during implementation): Document observed cleanup/persistence boundaries for interrupted attached-bag operations. Keep stronger proposal requirements separate from characterization and record any unresolved acceptance blocker. |
| F6.1 | Planned; no completion evidence | Provisional AutoStashGestureTests / EntityAttachedContainerGestureTests scenario F6.1 (replace with actual method names during implementation): Test block interaction start for generic containers, bloomeries, and crates with stashable contents; assert returned value, handling, and state. |
| F6.2 | Planned; no completion evidence | Provisional AutoStashGestureTests / EntityAttachedContainerGestureTests scenario F6.2 (replace with actual method names during implementation): Cover all crate Ctrl/Shift combinations and the active-stashable-item exception preserving vanilla interaction. |
| F6.3 | Planned; no completion evidence | Provisional AutoStashGestureTests / EntityAttachedContainerGestureTests scenario F6.3 (replace with actual method names during implementation): Test disabled feature, server-side invocation, and no stashable items without starting a gesture or sending packets. |
| F6.4 | Planned; no completion evidence | Provisional AutoStashGestureTests / EntityAttachedContainerGestureTests scenario F6.4 (replace with actual method names during implementation): Drive start and subsequent steps just before and exactly at configured stash delay; assert zero versus one request and the correct target. Include a non-default configured delay. |
| F6.5 | Planned; no completion evidence | Provisional AutoStashGestureTests / EntityAttachedContainerGestureTests scenario F6.5 (replace with actual method names during implementation): Continue stepping after submission and assert no duplicate request; cover exact end-of-grace and just beyond it. |
| F6.6 | Planned; no completion evidence | Provisional AutoStashGestureTests / EntityAttachedContainerGestureTests scenario F6.6 (replace with actual method names during implementation): Cancel/stop before submission and restart, proving state reset and no stale request. Assert progress removal and appropriate handling. |
| F6.7 | Planned; no completion evidence | Provisional AutoStashGestureTests / EntityAttachedContainerGestureTests scenario F6.7 (replace with actual method names during implementation): Observe progress creation/update/removal and sound/animation invocation through completion and subsequent steps; isolate presentation callbacks without requiring rendering. |
| F6.8 | Planned; no completion evidence | Provisional AutoStashGestureTests / EntityAttachedContainerGestureTests scenario F6.8 (replace with actual method names during implementation): Drive the attached-container controller through registered tick callbacks, testing delay boundaries and exactly-once submission with the selected entity/slot. |
| F6.9 | Planned; no completion evidence | Provisional AutoStashGestureTests / EntityAttachedContainerGestureTests scenario F6.9 (replace with actual method names during implementation): Test attached gesture cancellation for right-button, Ctrl, or Shift release; entity/attachment selection changes; and feature disabling. Assert no delayed stale submission and progress cleanup. |
| F6.10 | Planned; no completion evidence | Provisional AutoStashGestureTests / EntityAttachedContainerGestureTests scenario F6.10 (replace with actual method names during implementation): Test repeated `Begin` on the same target, `Begin` on a different target, restart after cancellation, and disposal unregistering its listener/removing progress. |
| F6.11 | Planned; no completion evidence | Provisional AutoStashGestureTests / EntityAttachedContainerGestureTests scenario F6.11 (replace with actual method names during implementation): Test attached interaction entry gating and selection-box-to-slot mapping, including mounted-player controls, invalid selection, and unavailable client controller. |
| F7.1 | Planned; no completion evidence | Provisional AutoStashServerNetworkTests scenario F7.1 (replace with actual method names during implementation): Deliver an allowed block request through the registered handler and assert actual expected movement. |
| F7.2 | Planned; no completion evidence | Provisional AutoStashServerNetworkTests scenario F7.2 (replace with actual method names during implementation): Deliver a denied-access request with otherwise transferable contents and assert no movement, session acquisition, or dirty/persistence effects. |
| F7.3 | Planned; no completion evidence | Provisional AutoStashServerNetworkTests scenario F7.3 (replace with actual method names during implementation): Test missing AutoStash behavior and missing target, retaining safe no-op expectations and the existing missing-position test. |
| F7.4 | Planned; no completion evidence | Provisional AutoStashServerNetworkTests scenario F7.4 (replace with actual method names during implementation): Exercise crate, generic-container, and bloomery dispatch with outcomes that establish the correct target-specific path. |
| F7.5 | Planned; no completion evidence | Provisional AutoStashServerNetworkTests scenario F7.5 (replace with actual method names during implementation): Deliver entity requests for missing entity, missing attachable behavior, and invalid attachment indices; assert no transfer. |
| F7.6 | Planned; no completion evidence | Provisional AutoStashServerNetworkTests scenario F7.6 (replace with actual method names during implementation): Test the six-block distance boundary and a position just outside it with otherwise valid attached contents; assert accepted versus rejected movement. |
| F7.7 | Planned; no completion evidence | Provisional AutoStashServerNetworkTests scenario F7.7 (replace with actual method names during implementation): Change target contents/capacity after client eligibility but before server dispatch, then assert the server uses current contents and does not rely on the previous client assessment. |
| F8.1 | Planned; no completion evidence | Provisional AutoStashHelpTests / AutoStashInstallationTests / AutoStashHarmonyIntegrationTests scenario F8.1 (replace with actual method names during implementation): Test container help for full matching target, no matching items, and disabled AutoStash; assert exact action codes and absence/presence of display stacks. |
| F8.2 | Planned; no completion evidence | Provisional AutoStashHelpTests / AutoStashInstallationTests / AutoStashHarmonyIntegrationTests scenario F8.2 (replace with actual method names during implementation): Test crate help hotkeys and returned representative stacks; preserve deduplication assertions. |
| F8.3 | Planned; no completion evidence | Provisional AutoStashHelpTests / AutoStashInstallationTests / AutoStashHarmonyIntegrationTests scenario F8.3 (replace with actual method names during implementation): Prove returned display stacks are independent clones by mutating them and checking source inventories remain unchanged. |
| F8.4 | Planned; no completion evidence | Provisional AutoStashHelpTests / AutoStashInstallationTests / AutoStashHarmonyIntegrationTests scenario F8.4 (replace with actual method names during implementation): Cover attached help append/no-append behavior, Ctrl+Shift help, and preservation of pre-existing interactions. |
| F8.5 | Planned; no completion evidence | Provisional AutoStashHelpTests / AutoStashInstallationTests / AutoStashHarmonyIntegrationTests scenario F8.5 (replace with actual method names during implementation): Test behavior amendment for supported containers, crates, and bloomeries, exclusion of unsupported/null-code entries, correct ordering in both behavior arrays, and preservation of existing behaviors. |
| F8.6 | Planned; no completion evidence | Provisional AutoStashHelpTests / AutoStashInstallationTests / AutoStashHarmonyIntegrationTests scenario F8.6 (replace with actual method names during implementation): Repeat amendment and assert no duplicate AutoStash or crate-bridge behaviors. |
| F8.7 | Planned; no completion evidence | Provisional AutoStashHelpTests / AutoStashInstallationTests / AutoStashHarmonyIntegrationTests scenario F8.7 (replace with actual method names during implementation): Add a focused integration fixture that installs actual Harmony patches and invokes engine crate/bloomery interaction entry points; verify handled interactions suppress the original and unhandled interactions reach vanilla behavior. Include attached interaction/help hooks where feasible in the same isolated integration harness. |
| F8.8 | Planned; no completion evidence | Provisional AutoStashHelpTests / AutoStashInstallationTests / AutoStashHarmonyIntegrationTests scenario F8.8 (replace with actual method names during implementation): Restore all installed patches in cleanup and assert no cross-test contamination. If the engine environment prevents automated integration, record the blocker and leave coverage incomplete rather than substituting helper-only tests. |
| F9.1 | Planned; no completion evidence | Provisional Final coverage reconciliation scenario F9.1 (replace with actual method names during implementation): Reconcile every F1–F8 scenario against implemented test names and outcomes in the evidence document; ensure all eight original findings remain traceable. |
| F9.2 | Planned; no completion evidence | Provisional Final coverage reconciliation scenario F9.2 (replace with actual method names during implementation): Run the full focused AutoStash suite, the separate AutoStash integration suite, and the repository unit suite through subagents to detect shared-fixture regressions. Record exact commands, assembly versions, pass/fail/skip counts, and relevant warnings. |
| F9.3 | Planned; no completion evidence | Provisional Final coverage reconciliation scenario F9.3 (replace with actual method names during implementation): Review assertion sensitivity: for every named scenario identify the behavioral change its assertions detect. Do not use test-count growth as the acceptance metric. |
| F9.4 | Planned; no completion evidence | Provisional Final coverage reconciliation scenario F9.4 (replace with actual method names during implementation): Obtain an independent final audit of scenario completeness, fixture realism, negative-test isolation, lifecycle assertions, and production-code scope. |
| F9.5 | Planned; no completion evidence | Provisional Final coverage reconciliation scenario F9.5 (replace with actual method names during implementation): Resolve all intended-versus-observed discrepancies explicitly. Document minimal reproductions and any separately authorized fixes; do not silently defer or skip required cases. |
| F9.6 | Planned; no completion evidence | Provisional Final coverage reconciliation scenario F9.6 (replace with actual method names during implementation): Confirm no inventory/transfer redesign was introduced, no game was launched, and automated results are not represented as live acceptance. |

## Second review

The implementer reviewed source diffs and helper implementations against F1.1–F1.10 after implementation. Checks included narrowing changed-slot exceptions so unrelated hotbar/backpack and nonmatching slots must remain unchanged; exact collectible identity for real merges; snapshots retaining copied attribute bytes and resolving current indexed slots for conservation; negative bloomery prerequisites; crate initializer ownership; and cleanup/collection isolation. A suitability fixture initially supplied a mock API to an engine constructor requiring world/calendar infrastructure; it now follows the existing inventory fixture pattern of assigning Api after construction. No production code or transfer algorithm was changed. Independent audit concluded Phase 1 fully satisfied after the final rerun and traceability reconciliation.

## Concrete assertion-test mapping

All names below are methods in AutoStashTransferTests unless qualified otherwise. Each is included in the focused AutoStash command; F1.4 and F1.6 share the true-merge cases, while the explicitly separate code-only case is in AutoStashFixtureTests.

F1.4 and F1.6 true-merge assertions:

- `AutoStashToGenericContainer_MatchingItemsInBackpack_StashesToContainer`
- `AutoStashToGenericContainer_DoesNotUseClientTransferApi`
- `AutoStashToGenericContainer_MatchingItemsInHotbar_StashesToContainer`
- `AutoStashToGenericContainer_ItemsInBothInventories_StashesBoth`
- `AutoStashToGenericContainer_FirstSlotPartiallyFull_UsesAdditionalSlot`
- `AutoStashToGenericContainer_FirstMatchingSlotFull_UsesAvailableMatchingSlot`
- `AutoStashToGenericContainer_MultipleMatchingTypes_StashesAll`
- `AutoStashToGenericContainer_OnlyMatchingTypesStashed_NonMatchingRemains`
- `AutoStashToCrate_MatchingItems_StashesToCrate`
- `AutoStashToCrate_MultipleItemTypes_OnlyMatchingTypeStashed`

F1.5 no-op assertions:

- `AutoStashToGenericContainer_EmptyContainer_ReturnsFalse`
- `AutoStashToGenericContainer_NoMatchingItems_ReturnsFalse`
- `AutoStashToGenericContainer_PlayerInventoryEmpty_ReturnsFalse`
- `AutoStashToCrate_EmptyCrate_ReturnsFalse`
- `AutoStashToCrate_PlayerHasDifferentItems_ReturnsFalse`
- `AutoStashToGenericContainer_NoExceptionOnEmptyInventories`
- `AutoStashToCrate_NoExceptionOnEmptyInventories`
- `TransferService_NoMatchingItemsOrCapacity_DoesNotOpenSession`
- `GetStashableItems_FullMatchingContainer_ReturnsEmptySet`

F1.7 negative prerequisites and paired validity controls in GetStashableItemsBloomeryTests:

- `GetStashableItems_PlayerHasNonCombustibleItems_ReturnsEmptySet`
- `GetStashableItems_PlayerHasLowTempCombustible_ReturnsEmptySet`
- `GetStashableItems_BloomeryIsBurning_ReturnsEmptySet`
- `GetStashableItems_BloomeryOutputNotEmpty_ReturnsEmptySet`

The two isolated classification negatives seed existing fuel, leaving the ore destination empty. Their paired positive controls change only the candidate to valid ore properties and require that the same source/target fixture is accepted. This distinguishes invalid classification from an unrelated gate or destination incompatibility.

## Independent completion audit

The phase1_audit subagent applied the complete audit-stage-completion workflow after the implementer second review. It independently read both skills, the full tasklist and linked proposal, changed/new test sources, engine crate initialization source, and durable baseline/final receipts. Verdict: Phase 1 fully satisfied, high confidence; F1.1–F1.10 individually satisfied. No required implementation or evidence gaps remain.

The concrete method-traceability request was resolved in this evidence document. A suspected low-temperature masking issue was withdrawn after inspecting the source item properties: it is invalid ore, and seeded fuel does not impose incompatible ore equality. Paired validity controls were nevertheless added, independently reviewed and verified in the final 104/300 runs. Later scenario coverage remains planned and is not included in this completion claim.

Only after this verdict were Phase 1 task/completion markers updated. No production sources changed; no game launched.
