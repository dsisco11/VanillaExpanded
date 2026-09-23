using System;
using Vintagestory.API.Common;

namespace VanillaExpanded.QuickTools;

/// <summary>Describes a displayed winner and the disposable slot hint used to revalidate it.</summary>
public sealed record QuickToolCandidate(string EntryId, IInventory Inventory, int SlotIndex, ItemSlot Slot, ItemStack Stack, int Tier, int RemainingDurability)
{
    /// <summary>Gets the whole-stack quantity displayed when the candidate was resolved.</summary>
    public int StackSize { get; } = Stack.StackSize;

    #region Validation
    /// <summary>Checks slot topology, stack identity, eligibility and current provider choice before a request.</summary>
    public bool Matches(QuickToolCandidate? current) => current is not null
        && string.Equals(EntryId, current.EntryId, StringComparison.Ordinal)
        && ReferenceEquals(Inventory, current.Inventory)
        && SlotIndex == current.SlotIndex
        && ReferenceEquals(Slot, current.Slot)
        && ReferenceEquals(Stack, current.Stack)
        && StackSize == current.StackSize
        && Tier == current.Tier
        && RemainingDurability == current.RemainingDurability;
    #endregion
}
