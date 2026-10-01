using Vintagestory.API.Common;

namespace VanillaExpanded.Tests.Unit.AutoStashing.Support;

/// <summary>Records suitability queries while retaining the engine's ranking and destination selection.</summary>
internal sealed class ObservedInventory : InventoryGeneric
{
    public List<ItemSlot> RankedSlots { get; } = [];

    #region Public API
    /// <summary>Creates an ordinary engine inventory with observable suitability evaluation.</summary>
    public ObservedInventory(int count, ICoreAPI api) : base(count, "observed", "test", null!)
    {
        // Avoid world/calendar initialization while retaining API access for actual slot operations.
        Api = api;
    }

    /// <summary>Records the evaluated destination before using the engine's configured suitability delegate.</summary>
    public override float GetSuitability(ItemSlot sourceSlot, ItemSlot targetSlot, bool isMerge)
    {
        RankedSlots.Add(targetSlot);
        return base.GetSuitability(sourceSlot, targetSlot, isMerge);
    }
    #endregion
}
