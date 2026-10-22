using System.Collections.Generic;
using Vintagestory.API.Common;

namespace VanillaExpanded.AutoStashing.Planning;

/// <summary>Restricts an operation to the crate's first initial item type; an empty crate accepts none.</summary>
internal sealed class CratePolicy : MatchingContentsPolicy
{
    public override EnumHandling StartHandling => EnumHandling.PreventSubsequent;
    #region Public API
    /// <summary>Captures only the first populated crate slot while preserving the engine crate inventory's restrictions.</summary>
    public CratePolicy(InventoryBase inventory) : base(InitialContents(inventory)) { }

    public override bool CanStartInteraction(IPlayerInventoryManager owner, BlockSelection? selection = null,
        IPlayer? player = null, bool forHelp = false, AutoStashAssessment? assessment = null)
    {
        if (forHelp || player is null) return true;
        if (!player.Entity.Controls.CtrlKey || !player.Entity.Controls.ShiftKey) return false;
        if (owner.ActiveHotbarSlot?.Itemstack is not ItemStack active) return true;
        System.ArgumentNullException.ThrowIfNull(assessment);
        return !assessment.AvailableItemIds.Contains(active.Collectible.Id);
    }
    #endregion

    #region Private
    /// <summary>Provides at most one initial stack, never adding types discovered during transfer.</summary>
    private static IEnumerable<ItemStack> InitialContents(InventoryBase inventory)
    {
        if (inventory.FirstNonEmptySlot?.Itemstack is ItemStack stack) yield return stack;
    }
    #endregion
}
