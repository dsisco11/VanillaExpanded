using System;

using VanillaExpanded.ItemSlotIndicators;

using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace VanillaExpanded.LiquidContainerIndicators;

/// <summary>Shows the fraction of a liquid container's capacity currently occupied.</summary>
internal sealed class LiquidContainerIndicatorProvider : IItemSlotIndicatorProvider
{
    #region Public API
    /// <summary>Reads current litres on each query so transfers are reflected immediately.</summary>
    public bool TryGetIndicator(ItemSlot slot, out ItemSlotIndicator indicator)
    {
        indicator = default;
        if (slot.Itemstack is not { Collectible: BlockLiquidContainerBase container } stack)
        {
            return false;
        }

        float capacity = container.CapacityLitres;
        if (!float.IsFinite(capacity) || capacity <= 0) return false;

        // Let the owning container convert liquid portions to litres; stack count is not volume.
        float litres = container.GetCurrentLitres(stack);
        if (!float.IsFinite(litres)) return false;
        float fill = Math.Clamp(litres / capacity, 0, 1);
        // Empty containers are useful collection vessels, so zero fill needs no warning overlay.
        indicator = new ItemSlotIndicator(fill, IndicatorColorPallette.WithOpacity(
            ColorUtilEx.MultiLerp(IndicatorColorPallette.WaterColors.AsSpan(), fill), 0.5f));
        return true;
    }
    #endregion
}
