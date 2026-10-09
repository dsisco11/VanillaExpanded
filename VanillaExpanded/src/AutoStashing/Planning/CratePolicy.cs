using System.Collections.Generic;
using Vintagestory.API.Common;

namespace VanillaExpanded.AutoStashing.Planning;

/// <summary>Restricts an operation to the crate's first initial item type; an empty crate accepts none.</summary>
internal sealed class CratePolicy : MatchingContentsPolicy
{
    #region Public API
    /// <summary>Captures only the first populated crate slot while preserving the engine crate inventory's restrictions.</summary>
    public CratePolicy(InventoryBase inventory) : base(InitialContents(inventory)) { }
    #endregion

    #region Private
    /// <summary>Provides at most one initial stack, never adding types discovered during transfer.</summary>
    private static IEnumerable<ItemStack> InitialContents(InventoryBase inventory)
    {
        if (inventory.FirstNonEmptySlot?.Itemstack is ItemStack stack) yield return stack;
    }
    #endregion
}
