using Vintagestory.API.Common;

namespace VanillaExpanded.AutoStashing.Targets;

/// <summary>Adapts a resolved engine inventory with an owned session and engine slot modification callbacks.</summary>
internal class InventoryAutoStashTarget : AutoStashTarget
{
    private readonly AutoStashInventorySession session;
    public override IInventory Inventory { get; }

    #region Public API
    /// <summary>Captures a stable inventory reference without opening it.</summary>
    public InventoryAutoStashTarget(IInventory inventory)
    {
        Inventory = inventory;
        session = new AutoStashInventorySession(inventory);
    }

    /// <summary>Acquires this inventory's session while preserving caller ownership.</summary>
    public override void Acquire(IPlayerInventoryManager owner) => session.Acquire(owner);

    /// <summary>Bare inventories rely on engine slot callbacks; block targets add their own synchronization.</summary>
    public override void FinalizeChanges() { }

    /// <summary>Closes only the session acquired by this adapter.</summary>
    public override void Release(IPlayerInventoryManager owner) => session.Release(owner);
    #endregion
}
