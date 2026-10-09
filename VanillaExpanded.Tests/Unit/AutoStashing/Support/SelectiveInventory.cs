using Vintagestory.API.Common;

namespace VanillaExpanded.Tests.Unit.AutoStashing.Support;

/// <summary>Controls inventory-level containment while leaving suitability and slot mutation engine-owned.</summary>
internal sealed class SelectiveInventory : InventoryGeneric
{
    public System.Func<ItemSlot, bool> Accept { get; set; } = _ => true;

    #region Public API
    /// <summary>Creates a real inventory without requiring world/calendar initialization.</summary>
    public SelectiveInventory(int count) : base(count, "selective", "test", null!) { }

    /// <summary>Applies a destination-specific containment rule before the engine's normal restriction.</summary>
    public override bool CanContain(ItemSlot sinkSlot, ItemSlot sourceSlot)
    {
        return Accept(sinkSlot) && base.CanContain(sinkSlot, sourceSlot);
    }
    #endregion
}
