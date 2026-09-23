# Quick-tool investigation second review

Reviewed 2026-09-23 against the full [proposal](../../QuickToolRadialMenu.proposal.md), selected phase 1 contract in the [checklist](../../QuickToolRadialMenu.todo), [implementation contract](ImplementationContract.md), and [installed API evidence](InventoryContractEvidence.md). This is the implementer's second pass, not the independent completion audit.

## Corrections made before independent audit

- Reconciled D1-D7 into the source proposal and recorded concrete eligibility, stable 33-category mapping, fallback, manual-change invalidation, and original-item restoration. The user's lower-durability and cached-mesh/no-performance-testing requirements remain controlling.
- Distinguished full-quad procedural rendering from the selected cached combined mesh approach; later state changes must not rebuild geometry.
- Resolved window focus through the installed public ScreenManager.Platform.IsFocused seam; a permanent focus callback without an unregister path is not the selected implementation.
- Established that the installed hotbar also contains skill/offhand slots, and that backpack content persistence requires OnItemSlotModified. Candidate enumeration cannot use inventory.Count alone as an eligibility rule.
- Replaced vague/protected native publication calls with public BroadcastPlayerData(true) plus BroadcastHotbarSlot. Preserved the extracted-stack notification argument after all assignments.
- Separated rejected precommit requests from committed requests whose callbacks or synchronization need reconciliation. Arbitrary callback effects are not rolled back, and no crash durability is claimed.
- Extended the isolated real-slot fixture from one three-slot move to complete A-B-A, A-B-C-A, empty-hand, and stale-reference cases. Eligibility flags are fixture inputs; actual slot validation and native persistence are not falsely reported as tested.
- Kept later rendering, equipment, networking, package, and live-runtime obligations unchecked. Retained documentation traceability and reproducible installed evidence rather than broad unneeded builds or performance measurements.

- Follow-up review made server active-slot and held-reference invalidation explicit, including switching away and back between requests. Client-local disablement sends a generation-scoped EndSession message; it never reverses an already committed operation.

## Requirement reconciliation

Every phase 1 item maps to the implementation contract's task-to-document table. The controlling proposal and phase scope were read directly; source sections were consulted around their definitions and constraints. Installed signatures/IL and passing fixture output support the selected primitive movement design. The expected intermediate state is a concrete design and evidence record without production menu or equipment code.

Independent completion audit remains required before completion markers or goal status change. No task is considered complete solely on this second review.