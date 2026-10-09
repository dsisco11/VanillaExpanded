using System.Linq;
using Vintagestory.API.Common;

namespace VanillaExpanded.AutoStashing.Targets;

/// <summary>Tracks ownership of an engine inventory session without treating it as a lock or transaction.</summary>
internal sealed class AutoStashInventorySession
{
    private readonly IInventory inventory;
    private bool owned;

    #region Public API
    /// <summary>Retains the exact resolved inventory for matching open/close calls.</summary>
    public AutoStashInventorySession(IInventory inventory) => this.inventory = inventory;

    /// <summary>Opens only a session not already owned by the caller; opening may change live inventory contents.</summary>
    public void Acquire(IPlayerInventoryManager owner)
    {
        owned = !(owner.OpenedInventories?.Contains(inventory) ?? false);
        if (owned) owner.OpenInventory(inventory);
    }

    /// <summary>Closes an operation-owned session once, leaving caller-owned sessions open.</summary>
    public void Release(IPlayerInventoryManager owner)
    {
        if (!owned) return;
        // Clear ownership before calling external cleanup so a throwing close cannot be repeated accidentally.
        owned = false;
        owner.CloseInventoryAndSync(inventory);
    }
    #endregion
}
