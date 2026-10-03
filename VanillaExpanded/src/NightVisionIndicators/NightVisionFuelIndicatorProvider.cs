using System;

using HarmonyLib;

using VanillaExpanded.ItemSlotIndicators;

using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace VanillaExpanded.NightVisionIndicators;

/// <summary>Shows remaining night-vision fuel relative to the device's own capacity.</summary>
internal sealed class NightVisionFuelIndicatorProvider : IItemSlotIndicatorProvider
{
    // The game exposes fuel publicly but keeps capacity protected; cache access without copying its value.
    private static readonly AccessTools.FieldRef<ItemNightvisiondevice, float> Capacity =
        AccessTools.FieldRefAccess<ItemNightvisiondevice, float>("fuelHoursCapacity");

    #region Public API
    /// <summary>Samples fuel on each query so consumption and refueling appear immediately.</summary>
    public bool TryGetIndicator(ItemSlot slot, out ItemSlotIndicator indicator)
    {
        indicator = default;
        if (slot.Itemstack is not { Collectible: ItemNightvisiondevice device } stack) return false;

        float capacity = Capacity(device);
        double remaining = device.GetFuelHours(stack);
        if (!float.IsFinite(capacity) || capacity <= 0 || !double.IsFinite(remaining)) return false;

        // Refueling can exceed nominal capacity; clamp the visual without changing stored fuel.
        float fill = (float)Math.Clamp(remaining / capacity, 0, 1);
        var color = ColorUtilEx.MultiLerp(IndicatorColorPallette.FreshnessColors.AsSpan(), fill, smooth: true);
        indicator = new ItemSlotIndicator(fill == 0 ? 1 : fill,
            IndicatorColorPallette.WithOpacity(color, fill == 0 ? 0.3f : 0.5f));
        return true;
    }
    #endregion
}
