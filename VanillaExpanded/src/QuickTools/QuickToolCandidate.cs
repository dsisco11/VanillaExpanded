using System;
using Vintagestory.API.Common;

namespace VanillaExpanded.QuickTools;

/// <summary>Describes a displayed winner and the disposable slot hint used to revalidate it.</summary>
public sealed record QuickToolCandidate(string EntryId, IInventory Inventory, int SlotIndex, ItemSlot Slot, ItemStack Stack, int Tier, int RemainingDurability)
{
    /// <summary>Gets the whole-stack quantity displayed when the candidate was resolved.</summary>
    public int StackSize { get; } = Stack.StackSize;
    private ItemStack Snapshot { get; } = Stack.Clone();

    #region Validation
    /// <summary>Checks slot topology, snapshot-equivalent contents, eligibility and current provider choice before movement.</summary>
    public bool Matches(QuickToolCandidate? current) => current is not null
        && string.Equals(EntryId, current.EntryId, StringComparison.Ordinal)
        && ReferenceEquals(Inventory, current.Inventory)
        && SlotIndex == current.SlotIndex
        && ReferenceEquals(Slot, current.Slot)
        && StackSize == current.StackSize
        && Tier == current.Tier
        && RemainingDurability == current.RemainingDurability
        && HasEquivalentStack(current.Stack);

    /// <summary>Accepts an unchanged local reference or one uniquely recreated by server synchronization.</summary>
    private bool HasEquivalentStack(ItemStack current)
    {
        if (ReferenceEquals(Stack, current)) return true;
        try { return Snapshot.Equals(Slot.Inventory.Api.World, current); }
        catch (Exception) { return false; }
    }
    #endregion
}
