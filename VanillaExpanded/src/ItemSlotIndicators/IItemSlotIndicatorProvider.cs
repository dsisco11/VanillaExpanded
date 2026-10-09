using Vintagestory.API.Common;

namespace VanillaExpanded.ItemSlotIndicators;

/// <summary>Supplies feature-specific indicator data without owning GUI rendering.</summary>
internal interface IItemSlotIndicatorProvider
{
    /// <summary>Returns false when this provider has nothing to indicate for the slot.</summary>
    bool TryGetIndicator(ItemSlot slot, out ItemSlotIndicator indicator);
}