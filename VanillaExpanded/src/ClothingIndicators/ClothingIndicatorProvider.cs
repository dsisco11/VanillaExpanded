using System;

using VanillaExpanded.ItemSlotIndicators;

using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace VanillaExpanded.ClothingIndicators;

/// <summary>Shows clothing condition with colors reflecting its contribution to warmth.</summary>
internal sealed class ClothingIndicatorProvider : IItemSlotIndicatorProvider
{
    #region Public API
    /// <summary>Reads initialized clothing condition without initializing or modifying the stack.</summary>
    public bool TryGetIndicator(ItemSlot slot, out ItemSlotIndicator indicator)
    {
        indicator = default;
        if (!VanillaExpandedModSystem.Config.EnableClothingIndicators) return false;
        ItemStack? stack = slot.Itemstack;
        CollectibleBehaviorWearable? wearable = stack?.Collectible.GetCollectibleBehavior<CollectibleBehaviorWearable>(withInheritance: true);
        if (wearable is null || wearable.IsArmorType(slot)
            || !stack!.Attributes.HasAttribute("condition")) return false;

        // Only warmth-bearing clothing uses this condition contract; armor retains vanilla durability.
        float warmth = wearable.GetMaxWarmth(slot);
        float condition = stack.Attributes.GetFloat("condition", float.NaN);
        if (!float.IsFinite(warmth) || warmth <= 0 || !float.IsFinite(condition)) return false;

        condition = Math.Clamp(condition, 0, 1);
        // Fill reports actual condition, while green begins at the game's full-warmth threshold.
        var color = ColorUtilEx.MultiLerp(IndicatorColorPallette.FreshnessColors.AsSpan(),
            Math.Clamp(condition / 0.5f, 0, 1), smooth: true);
        indicator = new ItemSlotIndicator(condition == 0 ? 1 : condition,
            IndicatorColorPallette.WithOpacity(color, condition == 0 ? 0.3f : 0.5f));
        return true;
    }
    #endregion
}
