using Vintagestory.API.Common;
using Vintagestory.GameContent;
using VanillaExpanded.AutoStashing.Planning;

namespace VanillaExpanded.AutoStashing.Targets;

/// <summary>Resolves bloomery availability and synchronizes its block entity without an inventory session.</summary>
internal sealed class BloomeryAutoStashTarget : AutoStashTarget
{
    private readonly BlockEntityBloomery bloomery;
    public override IInventory Inventory { get; }

    #region Public API
    /// <summary>Returns an available target while preserving the initial burning/output rejection gate.</summary>
    public static BloomeryAutoStashTarget? Resolve(BlockEntityBloomery bloomery)
    {
        InventoryGeneric? inventory = BloomeryAccessor.GetInventory(bloomery);
        return inventory is not null && BloomeryPolicy.IsAvailable(bloomery, inventory)
            ? new BloomeryAutoStashTarget(bloomery, inventory) : null;
    }

    /// <summary>Synchronizes applied or uncertain input changes using the existing full update notification.</summary>
    public override void FinalizeChanges() => bloomery.MarkDirty(true);
    #endregion

    #region Private
    /// <summary>Captures an available bloomery and its actual engine inventory.</summary>
    private BloomeryAutoStashTarget(BlockEntityBloomery bloomery, InventoryGeneric inventory)
    {
        this.bloomery = bloomery;
        Inventory = inventory;
    }
    #endregion
}
