using Vintagestory.API.Common;

namespace VanillaExpanded.AutoStashing.Planning;

/// <summary>Identifies one ordered inventory traversal and an optional mandatory destination.</summary>
internal sealed class AutoStashSourcePass
{
    public IInventory Inventory { get; }
    public int? RequiredSlot { get; }

    #region Public API
    /// <summary>Creates a pass without copying source slots or reserving their contents.</summary>
    public AutoStashSourcePass(IInventory inventory, int? requiredSlot = null)
    {
        Inventory = inventory;
        RequiredSlot = requiredSlot;
    }
    #endregion
}
