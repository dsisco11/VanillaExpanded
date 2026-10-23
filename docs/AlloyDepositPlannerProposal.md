# Proposal: Slot-Bounded Alloy Deposit Search

Status: Proposed; not implemented.

## Problem

[AlloyDepositStackPlanner.Resolve](../VanillaExpanded/src/AlloyCalculator/AlloyDepositStackPlanner.cs)
groups merge-compatible supplies, then selects forms in smallest-source-stack order.
Once selected, a form is not reconsidered. The planner returns no plan when those
choices require too many cooking slots, even if a different selection would fit.

The existing preference for consuming small stacks is useful, but must not prevent
an otherwise valid deposit.

## Goals

- Find a feasible arrangement when eligible supplies can satisfy the requested
  ingredient totals within the available cooking slots.
- Retain existing successful greedy choices and deterministic ordering.
- Preserve a valid concrete plan during execution rather than reshuffling it.
- Keep planning read-only and revalidate live inventory access during execution.
- Reject genuinely impossible arrangements without making partial transfers.

## Non-goals

- Changing alloy ratios, target quantities, or whole-item rounding.
- Supporting arbitrary metal yields beyond the current ingredient-count contract.
- Redesigning storage discovery, ownership, or machine access policy.
- Changing fuel selection or guaranteeing space for returning incorrect contents.
- Finding a globally optimal arrangement by movement count or inventory tidiness.

## Reproduction

The failing regression is
[Resolve_ThreeMetalAlloyWithOptionalForms_FindsFourSlotPlan](../VanillaExpanded.Tests/Unit/AlloyCalculator/AlloyDepositPlanTests.cs).

The imaginary alloy uses relative weights 75:50:25, normalized to approximately
50%, 33.33%, and 16.67%. The UI's integer percentages are 50/33/17.
At a target of 100 units, current whole-item rounding requests 10 A, 7 B,
and 4 C items, representing 105 input units. This proposal does not change that
rounding or claim that the rounded mixture exactly matches the ideal ratios.

All forms in this synthetic scenario are eligible five-unit items:

| Metal | Form | Available |
|-------|------|-----------|
| A | Bits | 15 |
| A | Nuggets | 10 |
| A | Chunks | 5 |
| B | Bits | 3 |
| B | Nuggets | 4 |
| C | Bits | 10 |

Metal-B nuggets were increased from the originally suggested three to four,
with approval, so a supply shortage does not mask the slot-allocation defect.

Greedy selection takes five A chunks and five A nuggets, three B bits and four
B nuggets, and four C bits: five nonmergeable forms for four slots.

A feasible alternative is:

| Cooking slot | Contents |
|--------------|----------|
| 0 | 10 A nuggets |
| 1 | 3 B bits |
| 2 | 4 B nuggets |
| 3 | 4 C bits |

The test currently fails because the planner returns null.

## Proposed Design

### 1. Preserve eligibility and grouping

Continue obtaining supplies through the existing inventory policy and native
cooking-slot `CanHold` checks. Do not use live `CanTakeFrom` merge/capacity checks
to reject forms before planned corrections have emptied their destinations.

Group by ingredient and actual merge-compatible stack equality, not merely item
code. Aggregate availability for each form, retaining the stable ordering derived
from its smallest source stack. Keep the current common cooking-slot capacity
model; heterogeneous slot capabilities are outside this change.

For a form:

```text
capacity = min(cooking-slot capacity, collectible stack limit)
contribution(k slots) = min(available items, k * capacity)
```

Nonpositive capacities cannot contribute.

### 2. Validate an existing concrete layout first

When execution receives a plan with concrete desired stacks, validate its exact
per-form totals, ingredient totals, slot indices, capacities, eligibility, and
available supplies before making new selections. If valid, retain it unchanged.

An alternative feasible supply combination must not invalidate an already valid
layout merely because a newly available smaller stack ranks earlier.

### 3. Retain greedy selection as the fast path

Try the current smallest-stack-first selection. If its exact quantities fit
within the slot budget, retain the result and existing balancing behavior.

If the selection cannot fit, invoke a bounded search instead of immediately
returning null.

### 4. Search form allocations with backtracking

Search ingredient groups in stable request order, and forms in their existing
preference order. Carry:

- Current ingredient and form index.
- Remaining item count for the current ingredient.
- Remaining slot budget.
- Selected forms and quantities for reconstruction.

For each form, try useful positive slot allocations, then zero to skip the form.
Try the allocation corresponding to the greedy contribution first; try smaller
allocations before skipping it. A selected form contributes up to its allocated
capacity, capped by its supply and the remaining requested count.

Taking the maximum usable contribution for a fixed allocation is sufficient
under the current model: forms supply interchangeable item counts for one
ingredient and have no cross-ingredient dependencies. Taking fewer items cannot
increase the capacity left for other ingredients. Charge only the slots actually
needed by that contribution; do not reserve empty slots.

On reaching an ingredient's exact total, advance to the next ingredient.
If later ingredients cannot fit, undo the choice and try the next allocation.
On satisfying every ingredient, return the first feasible selection.

This searches slot allocations, not every integer quantity. At most four forms
can participate in a vanilla four-slot crucible.

### 5. Prune impossible states

Before descending, reject branches when:

- Remaining supply for the ingredient is below its remaining request.
- Even an optimistic capacity bound cannot satisfy the request within the budget.
- The budget is below the number of remaining nonzero ingredients.
- The same failed state has already been explored.

Memoize failures using ingredient index, form index, remaining requested count,
and remaining slot budget. Keep the memo scoped to one immutable supply snapshot.
Previous selections need not enter the key while ingredients have disjoint
supplies and the only shared resource is the slot budget.

Do not introduce an arbitrary attempt limit that turns an incomplete search into
an insufficient-space result. Four slots bound selection depth, but candidate
count still affects runtime; measure representative large-form scenarios.

### 6. Materialize and balance

After finding a feasible selection, reuse the existing spare-slot distribution
and target construction. Each target must contain a single merge-compatible form,
respect its capacity, and preserve the requested ingredient totals exactly.

Do not move items, mutate source stacks, send packets, or consume fuel during
search. Execution continues to recollect supplies and enforce current ownership,
withdrawal, insertion, and transfer rules.

## Failure Semantics

- Insufficient eligible ingredient quantity remains an insufficient-items result.
- Sufficient quantity with no feasible allocation returns insufficient space.
- Failure caused by inventory changes during execution retains the existing live
  revalidation/transfer result; search does not provide transaction rollback.

## Implementation Scope

Keep the change within the existing planner where practical, using small private
helpers for grouping, search, and layout validation. Reuse the service's supply
eligibility rules; do not create a parallel inventory-access policy.

Update [the technical reference](AlloyCalculator.md) once the behavior is implemented.
Retain Debug-only diagnostic gating.

## Validation and Acceptance Criteria

1. The new three-metal regression passes without changing its supplies or requests.
2. All existing alloy-calculator tests continue to pass.
3. Cases requiring mixed forms still work; search must not insist on one form per metal.
4. Cases needing multiple slots for one form respect both slot and item stack limits.
5. A branch requiring backtracking across ingredient boundaries finds a valid plan.
6. Genuinely impossible arrangements return null without changing any source or target.
7. Eligible quantity shortages remain distinct from slot shortages.
8. A valid existing concrete layout is retained despite newly available preferred forms.
9. Repeated identical inputs produce identical plans.
10. Successful plans preserve exact ingredient totals, per-form availability, capacity,
    and unique destination slots.
11. Compare small synthetic cases with a simple exhaustive test oracle to catch false
    impossibility results.
12. Record runtime for representative high-form-count cases; avoid introducing an
    unmeasured cutoff that compromises search completeness.

Run the focused planner tests first, then the full alloy-calculator test group.
Finally, verify an in-game mixed-form deposit and confirm inventory conservation.

## Delivery Sequence

1. Add layout-preservation and search edge-case tests alongside the failing regression.
2. Implement concrete-layout validation and greedy fallback search.
3. Run focused tests and resolve any behavioral regressions.
4. Update the technical reference and verify an in-game deposit.
