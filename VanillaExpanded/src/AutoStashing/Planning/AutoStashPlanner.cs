using System;
using System.Linq;
using Vintagestory.API.Common;
using VanillaExpanded.AutoStashing.Transfers;

namespace VanillaExpanded.AutoStashing.Planning;

/// <summary>Selects one immediate live engine move and advances a bounded source-local cursor from actual outcomes.</summary>
internal sealed class AutoStashPlanner : IDisposable
{
    private readonly IWorldAccessor world;
    private readonly IInventory target;
    private readonly AutoStashPolicy policy;
    private readonly AutoStashCursor cursor;
    public bool DirectMergeFailed => cursor.DirectMergeFailed;

    #region Public API
    #region Operation cursor
    /// <summary>Creates an operation-local cursor without copying contents or opening sessions.</summary>
    public AutoStashPlanner(IWorldAccessor world, IInventory target, AutoStashPolicy policy, IInventory? backpack, IInventory? hotbar)
    {
        this.world = world;
        this.target = target;
        this.policy = policy;
        cursor = new AutoStashCursor(policy.GetSourcePasses(backpack, hotbar));
    }

    /// <summary>Selects at most one instruction from current contents; an outcome is required before another selection.</summary>
    public InventoryTransfer? GetNextTransfer()
    {
        // Selection cannot run ahead of mutation: the previous engine result determines retry state and live capacity.
        if (cursor.AwaitingOutcome) throw new InvalidOperationException("Advance the previous transfer before selecting another.");
        while (true)
        {
            // Keep processing the current remainder until it is exhausted or explicitly finished.
            // A new source resets local exclusions; policy passes preserve inventory and ore/fuel ordering.
            if (cursor.Source is null || cursor.Source.Empty)
            {
                if (!cursor.MoveNext()) return null;
                if (cursor.Source!.Empty || !policy.IsEligible(cursor.Source.Itemstack, cursor.Pass!))
                {
                    cursor.FinishSource();
                    continue;
                }
                // Eligibility exclusions belong to this source, not to the inventory or a later source stack.
                if (cursor.Pass!.RequiredSlot is null)
                    foreach (ItemSlot slot in target)
                        if (!slot.CanTakeFrom(cursor.Source, EnumMergePriority.AutoMerge)) cursor.Skipped.Add(slot);
            }

            ItemSlot source = cursor.Source!;
            // Read allowances from actual contents, so earlier partial moves affect subsequent requests.
            int quantity = policy.GetQuantity(source, cursor.Pass!);
            if (quantity <= 0)
            {
                cursor.FinishSource();
                continue;
            }
            InventoryTransfer? instruction;
            if (cursor.Pass!.RequiredSlot is int required)
            {
                // Mandatory routing never inherits preference fallback or ordinary/direct retry behavior.
                instruction = new InventoryTransfer(source, target[required]!, quantity);
            }
            else
            {
                ItemSlot? destination = SelectDestination(source);
                if (destination is null)
                {
                    // Only an exhausted rejected direct attempt warrants feedback. Full targets alone are silent.
                    // Aggregate across sources so later successful sources cannot hide an earlier failure.
                    cursor.DirectMergeFailed |= cursor.RejectedDirectMove;
                    cursor.FinishSource();
                    continue;
                }
                // Suitability callbacks can affect live contents; capture the request after selection as before.
                quantity = policy.GetQuantity(source, cursor.Pass!);
                if (quantity <= 0)
                {
                    cursor.FinishSource();
                    continue;
                }
                instruction = new InventoryTransfer(source, destination, quantity, EnumMouseButton.Left,
                    EnumModifierKey.SHIFT, cursor.DirectPhase ? EnumMergePriority.DirectMerge : EnumMergePriority.AutoMerge);
            }
            // Return one immediate instruction, leaving mutation and its exception/lifecycle handling to the owner.
            cursor.AwaitingOutcome = true;
            return instruction;
        }
    }

    /// <summary>Advances on actual results; rejected destinations remain excluded until finite positive progress.</summary>
    public void Advance(InventoryTransfer instruction, InventoryTransferResult result)
    {
        if (!cursor.AwaitingOutcome || !ReferenceEquals(cursor.Source, instruction.Source))
            throw new InvalidOperationException("No matching transfer is awaiting an outcome.");
        cursor.AwaitingOutcome = false;
        if (cursor.Pass!.RequiredSlot is not null)
        {
            // The original bloomery loop attempts each source only once in each ore/fuel pass.
            cursor.FinishSource();
            return;
        }
        ItemSlot destination = instruction.Destination;
        int moved = result.MovedQuantity;
        // Automatic selection visits each destination once for this source, including zero-move attempts.
        // Direct attempts use a separate exclusion set so automatic rejection does not remove direct alternatives.
        cursor.Skipped.Add(destination);
        if (instruction.Priority == EnumMergePriority.DirectMerge && moved > 0)
        {
            // A smaller remainder or changed merge state may make rejected destinations usable again.
            // Reset only after positive movement: between progress steps each direct destination is tried at most once.
            cursor.RejectedDirect.Clear();
            cursor.RejectedDirectMove = false;
        }
        if (instruction.RequestedQuantity == moved)
        {
            // This source is complete; do not carry a stale rejection into operation-wide failure feedback.
            cursor.FinishSource();
            return;
        }
        if (instruction.Priority == EnumMergePriority.DirectMerge && moved == 0)
        {
            // Exclude rejection even if the engine renews its direct-priority request, preventing an endless retry.
            cursor.RejectedDirect.Add(destination);
            cursor.RejectedDirectMove = true;
        }
        else if (instruction.Priority == EnumMergePriority.AutoMerge && moved == 0
            && result.RequiredPriority == EnumMergePriority.DirectMerge)
        {
            // Defer confirmation-strength merges until all automatic alternatives have been exhausted.
            cursor.DeferredDirect.Add(destination);
        }
        else if (instruction.Priority == EnumMergePriority.DirectMerge && moved > 0
            && destination.CanTakeFrom(instruction.Source, EnumMergePriority.DirectMerge))
        {
            // Finish this still-usable destination first. Each successful retry consumes source items.
            cursor.DeferredDirect.Insert(0, destination);
        }
    }

    #endregion

    #region Capacity probes
    /// <summary>Probes current capacity without sessions, retaining exclusive valid-index preference preflight.</summary>
    public static bool HasWork(IInventory target, AutoStashPolicy policy, IInventory? backpack, IInventory? hotbar)
    {
        // This is a plausibility probe, not a reservation; execution reads the state resulting from session opening.
        foreach (AutoStashSourcePass pass in policy.GetSourcePasses(backpack, hotbar))
        {
            foreach (ItemSlot source in pass.Inventory)
            {
                if (CanTransfer(target, policy, source, pass, includeDirect: true)) return true;
            }
        }
        return false;
    }

    /// <summary>Probes one live source using policy routing and engine eligibility, without selecting or executing a move.</summary>
    public static bool CanTransfer(IInventory target, AutoStashPolicy policy, ItemSlot source,
        AutoStashSourcePass pass, bool includeDirect)
    {
        if (source.Empty || !policy.IsEligible(source.Itemstack!, pass)) return false;
        // Mandatory input routing uses the original engine acceptance and live quantity rules, never ordinary fallback.
        if (pass.RequiredSlot is not null) return policy.GetQuantity(source, pass) > 0;
        if (policy.PreferredSlot?.GetIndex(source.Itemstack!, target.Count) is int preferred)
        {
            // Compatibility preflight remains exclusive; advisory callers retain automatic-only capacity detection.
            return target[preferred] is ItemSlot candidate && (includeDirect
                ? CanAccept(candidate, source) : candidate.CanTakeFrom(source, EnumMergePriority.AutoMerge));
        }
        return target.Any(slot => includeDirect
            ? CanAccept(slot, source) : slot.CanTakeFrom(source, EnumMergePriority.AutoMerge));
    }

    /// <summary>Checks candidate membership independently of destination capacity or workspace preparation.</summary>
    public static bool HasCandidates(AutoStashPolicy policy, IInventory? backpack, IInventory? hotbar)
    {
        // Candidate detection permits deferred workspace preparation without inventing a client/server capacity model.
        return policy.GetSourcePasses(backpack, hotbar).Any(pass => pass.Inventory.Any(source =>
            !source.Empty && policy.IsCandidate(source.Itemstack!, pass)));
    }

    #endregion

    #region Resource release
    /// <summary>Releases source enumerators on normal completion or failure.</summary>
    public void Dispose() => cursor.Dispose();
    #endregion
    #endregion

    #region Private
    /// <summary>Preserves preference, engine-ranked automatic selection, then deferred and all-live direct candidates.</summary>
    private ItemSlot? SelectDestination(ItemSlot source)
    {
        ItemSlot? destination = null;
        // Try a valid preference before engine ranking, but only while automatic alternatives remain.
        if (!cursor.DirectPhase && policy.PreferredSlot?.GetIndex(source.Itemstack!, target.Count) is int preferred)
        {
            ItemSlot? candidate = target[preferred];
            if (candidate is not null && !cursor.Skipped.Contains(candidate) && candidate.CanTakeFrom(source)) destination = candidate;
        }
        if (destination is null && !cursor.DirectPhase)
        {
            // Delegate ranking to the inventory, passing this source's automatic exclusions unchanged.
            var operation = new ItemStackMoveOperation(world, EnumMouseButton.Left, EnumModifierKey.SHIFT,
                EnumMergePriority.AutoMerge, source.StackSize);
            destination = target.GetBestSuitedSlot(source, operation, cursor.Skipped)?.slot;
        }
        if (destination is not null) return destination;
        // Once automatic selection is exhausted, remain in direct mode for this source.
        // Try deferred destinations in order, checking their current eligibility and rejection state.
        cursor.DirectPhase = true;
        destination = cursor.DeferredDirect.FirstOrDefault(slot => !cursor.RejectedDirect.Contains(slot)
            && slot.CanTakeFrom(source, EnumMergePriority.DirectMerge));
        if (destination is not null) cursor.DeferredDirect.Remove(destination);
        // Exhaust all live direct-eligible slots, including empty slots and those excluded from automatic selection.
        return destination ?? target.FirstOrDefault(slot => !cursor.RejectedDirect.Contains(slot)
            && slot.CanTakeFrom(source, EnumMergePriority.DirectMerge));
    }

    /// <summary>Checks live engine eligibility at either supported merge priority without predicting movement.</summary>
    private static bool CanAccept(ItemSlot destination, ItemSlot source)
        => destination.CanTakeFrom(source, EnumMergePriority.AutoMerge) || destination.CanTakeFrom(source, EnumMergePriority.DirectMerge);
    #endregion
}
