using System;
using System.Collections.Immutable;
using System.Numerics;

using VanillaExpanded.ItemSlotIndicators;

using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace VanillaExpanded.WateringCanIndicators;

/// <summary>Shows watering-can water levels with a blue fill or a red empty-can sliver.</summary>
internal sealed class WateringCanIndicatorProvider : IItemSlotIndicatorProvider
{
    private const float EmptyFill = 0.04f;
    private static readonly Vector4 EmptyColor = new(0.88f, 0.08f, 0.05f, 0.6f);
    private static readonly ImmutableArray<Vector4> WaterColors =
    [
        new(0.04f, 0.16f, 0.38f, 0.4f),
        new(0.45f, 0.8f, 0.98f, 0.4f)
    ];

    #region Public API
    /// <summary>Reads the current water level immediately, without caching pouring or refill changes.</summary>
    public bool TryGetIndicator(ItemSlot slot, out ItemSlotIndicator indicator)
    {
        indicator = default;
        ItemStack? stack = slot.Itemstack;
        if (stack?.Collectible is not BlockWateringCan can
            || !float.IsFinite(can.CapacitySeconds) || can.CapacitySeconds <= 0)
        {
            return false;
        }

        float remaining = can.GetRemainingWateringSeconds(stack);
        float fill = Math.Clamp(float.IsFinite(remaining) ? remaining / can.CapacitySeconds : 0, 0, 1);
        indicator = fill <= 0
            ? new ItemSlotIndicator(EmptyFill, EmptyColor)
            : new ItemSlotIndicator(fill, ColorUtilEx.MultiLerp(WaterColors.AsSpan(), fill));
        return true;
    }
    #endregion
}