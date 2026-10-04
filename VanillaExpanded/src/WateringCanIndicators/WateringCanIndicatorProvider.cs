using System;
using System.Numerics;

using VanillaExpanded.ItemSlotIndicators;
using VanillaExpanded.ItemSlotIndicators.Effects;

using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace VanillaExpanded.WateringCanIndicators;

/// <summary>Shows watering-can water levels with a blue fill or a full red empty-can warning.</summary>
internal sealed class WateringCanIndicatorProvider : IItemSlotIndicatorProvider
{
    private const float LowWaterOpacity = 0.5f;
    private const float FullWaterOpacity = 0.5f;
    private const float EmptyOpacity = 0.3f;

    #region Public API
    /// <summary>Reads the current water level immediately, without caching pouring or refill changes.</summary>
    public bool TryGetIndicator(ItemSlot slot, out ItemSlotIndicator indicator)
    {
        indicator = default;
        if (!VanillaExpandedModSystem.Config.EnableLiquidContainerIndicators) return false;
        ItemStack? stack = slot.Itemstack;
        if (stack?.Collectible is not BlockWateringCan can
            || !float.IsFinite(can.CapacitySeconds) || can.CapacitySeconds <= 0)
        {
            return false;
        }

        float remaining = can.GetRemainingWateringSeconds(stack);
        float fill = Math.Clamp(float.IsFinite(remaining) ? remaining / can.CapacitySeconds : 0, 0, 1);
        Vector4 color = fill <= 0
            ? IndicatorColorPallette.WithOpacity(IndicatorColorPallette.Red, EmptyOpacity)
            : IndicatorColorPallette.WithOpacity(
                ColorUtilEx.MultiLerp(IndicatorColorPallette.WaterColors.AsSpan(), fill),
                float.Lerp(LowWaterOpacity, FullWaterOpacity, fill));
        // Empty cans retain the full red warning; only actual water receives bounded liquid levels.
        indicator = new ItemSlotIndicator(fill <= 0 ? 1 : fill, color,
            fill <= 0 ? null : LiquidSloshIndicatorEffect.DrawRange);
        return true;
    }
    #endregion
}
