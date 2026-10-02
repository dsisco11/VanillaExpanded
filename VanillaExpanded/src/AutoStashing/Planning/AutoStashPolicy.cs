using System.Collections.Generic;
using Vintagestory.API.Common;

namespace VanillaExpanded.AutoStashing.Planning;

/// <summary>Owns target eligibility, ordered source passes and live quantity limits independently of mutation.</summary>
internal abstract class AutoStashPolicy
{
    public virtual PreferredSlotCompatibility? PreferredSlot => null;

    #region Public API
    /// <summary>Traverses backpack then hotbar, omitting absent inventories.</summary>
    public virtual IEnumerable<AutoStashSourcePass> GetSourcePasses(IInventory? backpack, IInventory? hotbar)
    {
        if (backpack is not null) yield return new AutoStashSourcePass(backpack);
        if (hotbar is not null) yield return new AutoStashSourcePass(hotbar);
    }

    /// <summary>Checks whether the current item belongs to the supplied source pass.</summary>
    public abstract bool IsEligible(ItemStack stack, AutoStashSourcePass pass);

    /// <summary>Returns a live quantity allowance without predicting engine mutation.</summary>
    public virtual int GetQuantity(ItemSlot source, AutoStashSourcePass pass) => source.StackSize;
    #endregion
}
