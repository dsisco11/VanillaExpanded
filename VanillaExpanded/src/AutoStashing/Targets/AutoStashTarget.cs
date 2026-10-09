using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;

namespace VanillaExpanded.AutoStashing.Targets;

/// <summary>Owns target preparation, execution acquisition, persistence and release independently of transfer planning.</summary>
internal abstract class AutoStashTarget
{
    public abstract IInventory Inventory { get; }
    public virtual bool IsPrepared => true;

    #region Public API
    /// <summary>Reads existing contents without acquiring a session or constructing an execution workspace.</summary>
    public virtual IEnumerable<ItemStack?> GetContents() => Inventory.Select(slot => slot.Itemstack);

    /// <summary>Prepares an execution inventory after candidate detection; ordinary inventories are already resolved.</summary>
    public virtual bool TryPrepare() => true;

    /// <summary>Acquires target-specific execution resources after plausible capacity has been established.</summary>
    public virtual void Acquire(IPlayerInventoryManager owner) { }

    /// <summary>Persists or synchronizes actual or uncertain changes before execution resources are released.</summary>
    public abstract void FinalizeChanges();

    /// <summary>Releases only resources acquired by this operation, including after finalization fails.</summary>
    public virtual void Release(IPlayerInventoryManager owner) { }
    #endregion
}
