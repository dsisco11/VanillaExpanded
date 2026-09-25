using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace VanillaExpanded.QuickTools;

/// <summary>Owns one client's temporary restoration history and ordinary inventory flips.</summary>
internal sealed class QuickToolEquipment
{
    private readonly IPlayerInventoryManager manager;
    private readonly ItemSlot physicalOffhand;
    private readonly QuickToolInventoryView view;
    private readonly Action<object> send;
    private readonly ITagRegistry<TagSet>? tagRegistry;
    private QuickToolSession? session;
    private bool operationActive;
    private long lifetimeVersion;

    /// <summary>Creates an equipment owner using the game's client inventory-packet sender.</summary>
    internal QuickToolEquipment(IPlayerInventoryManager manager, ItemSlot physicalOffhand, Action<object> send,
        bool allowGenericFixture = false, ITagRegistry<TagSet>? tagRegistry = null)
    {
        this.manager = manager;
        this.physicalOffhand = physicalOffhand;
        this.send = send;
        this.tagRegistry = tagRegistry;
        view = new QuickToolInventoryView(manager, allowGenericFixture);
    }

    #region Session state
    /// <summary>Gets whether a current temporary selection can be restored.</summary>
    internal bool HasSession => session is not null;
    /// <summary>Gets whether own native flip callbacks are currently running.</summary>
    internal bool OperationActive => operationActive;
    /// <summary>Clears transient history without moving an item.</summary>
    internal void Clear()
    {
        session = null;
        lifetimeVersion++;
    }
    /// <summary>Retains restoration history while the player temporarily uses another active-hand slot.</summary>
    internal void OnManualActiveSlotChanged() { }

    /// <summary>Validates references and the return route once when preparing restoration availability.</summary>
    internal bool ValidateRestoration()
        => !operationActive && session is not null && PlanRestoration(out _, out _) is not null;

    /// <summary>Validates either restoration history or moving the current active stack to ordinary storage.</summary>
    internal bool ValidateUnequip()
        => !operationActive && (session is not null ? PlanRestoration(out _, out _) is not null : PlanUnequip(out _, out _) is not null);
    #endregion

    #region Selection and restoration
    /// <summary>Reruns a known current provider and swaps its displayed winner into the active hand.</summary>
    internal QuickToolEquipmentResult Select(string entryId, QuickToolCandidate displayed)
    {
        if (operationActive || displayed is null) return QuickToolEquipmentResult.Rejected;
        ItemSlot? hand = view.ActiveHand();
        ItemSlot? originalAt = null;
        if (session is not null && !session.TryResolve(view, physicalOffhand, out hand, out originalAt))
        {
            session = null;
            return QuickToolEquipmentResult.SessionInvalidated;
        }
        if (hand is null) return QuickToolEquipmentResult.Rejected;

        IQuickToolCandidateProvider? provider = ResolveProvider(entryId);
        if (provider is null) return QuickToolEquipmentResult.Rejected;
        QuickToolCandidate? candidate;
        try { candidate = provider.Resolve(manager, physicalOffhand, hand); }
        catch (Exception) { return QuickToolEquipmentResult.Rejected; }
        if (candidate is null || !displayed.Matches(candidate)) return QuickToolEquipmentResult.Rejected;
        bool light = entryId == QuickToolLayout.LightId;
        if (!ReferenceEquals(view.Find(candidate.Stack, candidate.StackSize, physicalOffhand), candidate.Slot)
            || view.GetSource(ReferenceEquals(candidate.Inventory, manager.GetOwnInventory(Vintagestory.API.Config.GlobalConstants.backpackInvClassName)),
                candidate.SlotIndex, physicalOffhand, light) != candidate.Slot
            || (ReferenceEquals(candidate.Slot, physicalOffhand) && (!light || view.Offhand(physicalOffhand) is null)))
            return QuickToolEquipmentResult.Rejected;

        if (ReferenceEquals(candidate.Slot, hand)) return QuickToolEquipmentResult.NoOp;
        if (session is not null && ReferenceEquals(candidate.Stack, session.Original)) return Restore();
        return session is null ? EquipFirst(hand, candidate) : Switch(hand, candidate, originalAt);
    }

    /// <summary>Returns the current selection home or to D5 fallback and restores the original hand.</summary>
    internal QuickToolEquipmentResult Restore()
    {
        if (operationActive || session is null) return QuickToolEquipmentResult.Rejected;
        QuickToolMovementPlan? plan = PlanRestoration(out ItemSlot hand, out List<ItemSlot> targets);
        if (plan is null) return session is null ? QuickToolEquipmentResult.SessionInvalidated : QuickToolEquipmentResult.Rejected;
        return Execute(plan, hand, targets, () => session = null);
    }

    /// <summary>Restores tracked history or moves an unrelated active-hand stack off the hotbar.</summary>
    internal QuickToolEquipmentResult Unequip()
    {
        if (operationActive) return QuickToolEquipmentResult.Rejected;
        if (session is not null) return Restore();
        QuickToolMovementPlan? plan = PlanUnequip(out ItemSlot hand, out ItemSlot target);
        return plan is null ? QuickToolEquipmentResult.Rejected : Execute(plan, hand, [target], static () => { });
    }

    /// <summary>Resolves the tracked objects and validates a return route without changing inventory.</summary>
    private QuickToolMovementPlan? PlanRestoration(out ItemSlot hand, out List<ItemSlot> targets)
    {
        hand = null!;
        targets = [];
        if (session is null) return null;
        if (!session.TryResolve(view, physicalOffhand, out hand, out ItemSlot? originalAt))
        {
            session = null;
            return null;
        }
        QuickToolSession history = session;
        ItemSlot? returnSlot = ChooseReturnSlot(history.CurrentHome, hand, history.Current, originalAt);
        if (returnSlot is null) return null;

        var desired = new Dictionary<ItemSlot, ItemStack?>(ReferenceEqualityComparer.Instance)
        {
            [hand] = history.Original,
            [returnSlot] = history.Current
        };
        if (originalAt is not null && !ReferenceEquals(originalAt, returnSlot)) desired[originalAt] = null;
        QuickToolMovementPlan? plan = QuickToolMovementPlan.TryCreate(view, physicalOffhand, desired);
        if (plan is null) return null;
        targets.Add(returnSlot);
        if (originalAt is not null && !ReferenceEquals(originalAt, returnSlot)) targets.Add(originalAt);
        return plan;
    }

    /// <summary>Plans a single native flip that stores an active-hand stack in backpack storage before hotbar slots.</summary>
    private QuickToolMovementPlan? PlanUnequip(out ItemSlot hand, out ItemSlot target)
    {
        hand = view.ActiveHand()!;
        target = null!;
        if (hand is null || hand.Empty || view.Find(hand.Itemstack!, hand.Itemstack!.StackSize, physicalOffhand) != hand) return null;
        foreach (ItemSlot slot in view.UnequipSlots())
        {
            if (ReferenceEquals(slot, hand) || !slot.Empty) continue;
            try
            {
                if (hand.Itemstack!.StackSize > slot.MaxSlotStackSize || !slot.CanHold(hand)) continue;
            }
            catch (Exception) { continue; }
            var desired = new Dictionary<ItemSlot, ItemStack?>(ReferenceEqualityComparer.Instance)
            {
                [hand] = null,
                [slot] = hand.Itemstack
            };
            QuickToolMovementPlan? plan = QuickToolMovementPlan.TryCreate(view, physicalOffhand, desired);
            if (plan is not null)
            {
                target = slot;
                return plan;
            }
        }
        return null;
    }

    /// <summary>Starts history only after the first native flip produces the intended local arrangement.</summary>
    private QuickToolEquipmentResult EquipFirst(ItemSlot hand, QuickToolCandidate candidate)
    {
        ItemStack? original = hand.Itemstack;
        if (original is not null && !ReferenceEquals(view.Find(original, original.StackSize, physicalOffhand), hand))
            return QuickToolEquipmentResult.Rejected;
        var desired = new Dictionary<ItemSlot, ItemStack?>(ReferenceEqualityComparer.Instance)
        {
            [hand] = candidate.Stack,
            [candidate.Slot] = original
        };
        QuickToolMovementPlan? plan = QuickToolMovementPlan.TryCreate(view, physicalOffhand, desired);
        if (plan is null) return QuickToolEquipmentResult.Rejected;
        var newSession = new QuickToolSession(manager.ActiveHotbarSlotNumber, original, candidate.EntryId,
            candidate.Stack, new QuickToolSlotAddress(candidate.Inventory, candidate.SlotIndex));
        return Execute(plan, hand, [candidate.Slot], () => session = newSession);
    }

    /// <summary>Returns B, recovers original A if moved, then equips C with one native packet per flip.</summary>
    private QuickToolEquipmentResult Switch(ItemSlot hand, QuickToolCandidate candidate, ItemSlot? originalAt)
    {
        QuickToolSession history = session!;
        ItemSlot? returnSlot = ChooseReturnSlot(history.CurrentHome, hand, history.Current, originalAt, candidate.Slot);
        if (returnSlot is null) return QuickToolEquipmentResult.Rejected;
        var desired = new Dictionary<ItemSlot, ItemStack?>(ReferenceEqualityComparer.Instance)
        {
            [hand] = candidate.Stack,
            [returnSlot] = history.Current,
            [candidate.Slot] = history.Original
        };
        if (originalAt is not null && !ReferenceEquals(originalAt, returnSlot)) desired[originalAt] = null;
        QuickToolMovementPlan? plan = QuickToolMovementPlan.TryCreate(view, physicalOffhand, desired);
        if (plan is null) return QuickToolEquipmentResult.Rejected;
        var targets = new List<ItemSlot> { returnSlot };
        if (originalAt is not null && !ReferenceEquals(originalAt, returnSlot)) targets.Add(originalAt);
        targets.Add(candidate.Slot);
        return Execute(plan, hand, targets,
            () => history.Selected(candidate.EntryId, candidate.Stack, new QuickToolSlotAddress(candidate.Inventory, candidate.SlotIndex)));
    }

    /// <summary>Uses the recorded home when free or holding A, else the first compatible empty ordinary slot.</summary>
    private ItemSlot? ChooseReturnSlot(QuickToolSlotAddress homeAddress, ItemSlot hand, ItemStack current, ItemSlot? originalAt, ItemSlot? selected = null)
    {
        ItemSlot? home = view.Resolve(homeAddress, physicalOffhand);
        if (home is not null && EligibleReturn(home, hand, current, originalAt, selected, true)) return home;
        foreach (ItemSlot slot in view.OrdinarySlots())
            if (EligibleReturn(slot, hand, current, originalAt, selected, false)) return slot;
        return null;
    }

    /// <summary>Checks that a return slot is available before native pairwise checks repeat restrictions.</summary>
    private bool EligibleReturn(ItemSlot slot, ItemSlot hand, ItemStack current, ItemSlot? originalAt, ItemSlot? selected, bool recordedHome)
    {
        if (ReferenceEquals(slot, hand) || ReferenceEquals(slot, selected)
            || !view.ContainsTracked(slot, physicalOffhand)
            || (!slot.Empty && !(recordedHome && ReferenceEquals(slot, originalAt)))) return false;
        try { return current.StackSize <= slot.MaxSlotStackSize && slot.CanHold(hand); }
        catch (Exception) { return false; }
    }

    /// <summary>Runs a prechecked sequence without claiming unchanged inventory after an interrupted flip.</summary>
    private QuickToolEquipmentResult Execute(QuickToolMovementPlan plan, ItemSlot hand, IReadOnlyList<ItemSlot> targets, Action afterLocalSuccess)
    {
        operationActive = true;
        long operationLifetime = lifetimeVersion;
        try
        {
            QuickToolMovementStatus status = plan.Execute(hand, targets, send, () => lifetimeVersion == operationLifetime);
            if (status == QuickToolMovementStatus.Rejected) return QuickToolEquipmentResult.Rejected;
            if (status == QuickToolMovementStatus.Interrupted)
            {
                session = null;
                return QuickToolEquipmentResult.Interrupted;
            }
            afterLocalSuccess();
            if (session is not null && !session.TryResolve(view, physicalOffhand, out _, out _))
            {
                session = null;
                return QuickToolEquipmentResult.Interrupted;
            }
            return QuickToolEquipmentResult.LocallyApplied;
        }
        finally { operationActive = false; }
    }

    /// <summary>Dispatches only stable identifiers in the supported client provider set.</summary>
    private IQuickToolCandidateProvider? ResolveProvider(string id)
    {
        if (id == QuickToolLayout.LightId) return new LightCandidateProvider();
        if (tagRegistry is null && QuickToolLayout.TryGetTool(id, out EnumTool category)) return new ToolCandidateProvider(category);
        return QuickToolLayout.TryGetToolTagsFromId(id, out string[] toolTags) ? new ToolCandidateProvider(toolTags, tagRegistry: tagRegistry) : null;
    }
    #endregion
}

/// <summary>Reports provisional native movement and interruption without inventing server acknowledgement.</summary>
internal enum QuickToolEquipmentResult
{
    Rejected,
    NoOp,
    LocallyApplied,
    Interrupted,
    SessionInvalidated
}
